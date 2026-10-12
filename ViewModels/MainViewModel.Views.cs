using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using HashDash.Helpers;
using HashDash.Models;
using HashDash.Services;

namespace HashDash.ViewModels
{
    public enum FileViewMode { List, Directory, Thumbnails }

    /// <summary>
    /// The three presentations of the file list — plain list, directory tree and thumbnails.
    /// All three show the same Files collection and honour the same search/status filter;
    /// selection checkboxes in every view drive the same FileHashEntry.IsSelected flags.
    /// </summary>
    public partial class MainViewModel
    {
        private const string GlyphFolder  = "";
        private const string GlyphDevice  = "";
        private const string GlyphLibrary = "";
        private const string GlyphGroup   = "";

        private static readonly string[] SectionOrder =
        {
            IosPhotoLibraryService.SectionAlbums, "Shared Albums", IosPhotoLibraryService.SectionSavedByApp,
            IosPhotoLibraryService.SectionMediaTypes, IosPhotoLibraryService.SectionUtilities
        };

        private FileViewMode _viewMode = FileViewMode.List;
        private bool _treeDirty = true;
        private bool _thumbnailsDirty = true;
        private bool _viewRefreshPending;
        private bool _thumbnailReadsLogged;
        private int  _thumbnailColumns = 6;
        private ThumbnailService? _thumbnailService;
        private CancellationTokenSource _thumbnailCts = new();
        private readonly Dictionary<FileHashEntry, ThumbnailTile> _tiles = new(ReferenceEqualityComparer.Instance);

        public ObservableCollection<FolderNode>   FolderTree    { get; } = new();
        public ObservableCollection<ThumbnailRow> ThumbnailRows { get; } = new();

        // ── View mode ─────────────────────────────────────────────────────

        public FileViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (!SetProperty(ref _viewMode, value)) return;
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsDirectoryView));
                OnPropertyChanged(nameof(IsThumbnailView));
                RefreshActiveView();
            }
        }

        public bool IsListView
        {
            get => _viewMode == FileViewMode.List;
            set { if (value) ViewMode = FileViewMode.List; }
        }

        public bool IsDirectoryView
        {
            get => _viewMode == FileViewMode.Directory;
            set { if (value) ViewMode = FileViewMode.Directory; }
        }

        public bool IsThumbnailView
        {
            get => _viewMode == FileViewMode.Thumbnails;
            set { if (value) ViewMode = FileViewMode.Thumbnails; }
        }

        private void MarkViewsDirty()
        {
            _treeDirty       = true;
            _thumbnailsDirty = true;
            ScheduleViewRefresh();
        }

        private void OnFilterChanged()
        {
            _filesView.Refresh();
            FolderFilesView?.Refresh();
            _thumbnailsDirty = true;
            ScheduleViewRefresh();
        }

        /// <summary>Coalesces the many change notifications a 500-file batch load produces into one rebuild.</summary>
        private void ScheduleViewRefresh()
        {
            if (_viewRefreshPending) return;
            _viewRefreshPending = true;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _viewRefreshPending = false;
                RefreshActiveView();
            }, DispatcherPriority.Background);
        }

        private void RefreshActiveView()
        {
            if (_viewMode == FileViewMode.Directory && _treeDirty)  RebuildFolderTree();
            if (_viewMode == FileViewMode.Thumbnails && _thumbnailsDirty) RebuildThumbnails();
        }

        // ── Directory view ────────────────────────────────────────────────

        private FolderNode? _selectedFolder;
        public FolderNode? SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (!SetProperty(ref _selectedFolder, value)) return;
                FolderFilesView = value == null
                    ? null
                    : new ListCollectionView(value.AllFiles().ToList()) { Filter = FilterFiles };
                OnPropertyChanged(nameof(SelectedFolderTitle));
            }
        }

        public string SelectedFolderTitle =>
            _selectedFolder == null ? "" : $"{_selectedFolder.Name}  —  {_selectedFolder.TotalFiles:N0} files, {_selectedFolder.SizeDisplay}";

        private ICollectionView? _folderFilesView;
        public ICollectionView? FolderFilesView
        {
            get => _folderFilesView;
            private set => SetProperty(ref _folderFilesView, value);
        }

        /// <summary>
        /// Builds two trees: the source's real folder structure, and — for iOS — the Photos
        /// app's own grouping (albums, saved-by-app, media types, utilities) so evidence can
        /// be reviewed and selected exactly as the user saw it on the phone.
        /// </summary>
        private void RebuildFolderTree()
        {
            _treeDirty = false;
            string? previous = _selectedFolder?.Name;

            FolderTree.Clear();
            SelectedFolder = null;
            if (Files.Count == 0) return;

            bool isDevice = Files[0].IsDeviceSource;
            string rootName = string.IsNullOrEmpty(_baseSourceFolder) ? "Source" : _baseSourceFolder;
            var root = new FolderNode(isDevice ? $"{rootName.Trim('[', ']')} — device folders" : rootName,
                                      isDevice ? GlyphDevice : GlyphFolder) { IsExpanded = true };

            FolderNode? library = null;
            foreach (var f in Files)
            {
                string relative = PathHelper.GetRelativePath(f.FullPath, _baseSourceFolder);
                string[] parts  = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);

                var node = root;
                for (int i = 0; i < parts.Length - 1; i++)
                    node = node.GetOrAddChild(parts[i], GlyphFolder);
                node.Files.Add(f);

                foreach (string group in f.PhotoGroups)
                {
                    library ??= new FolderNode("Photos Library — as shown in the Photos app", GlyphLibrary) { IsExpanded = true };
                    string[] path = group.Split('/');
                    var g = library;
                    for (int i = 0; i < path.Length; i++)
                        g = g.GetOrAddChild(path[i], i == 0 ? GlyphLibrary : GlyphGroup);
                    g.Files.Add(f);
                }
            }

            root.Finalise();
            FolderTree.Add(root);

            if (library != null)
            {
                library.Finalise();
                var ordered = library.Children
                    .OrderBy(c => Array.IndexOf(SectionOrder, c.Name) is int i && i >= 0 ? i : SectionOrder.Length)
                    .ToList();
                library.Children.Clear();
                foreach (var section in ordered)
                {
                    section.IsExpanded = true;
                    library.Children.Add(section);
                }
                FolderTree.Insert(0, library);
            }

            RefreshFolderCheckStates();

            var select = FolderTree.SelectMany(Flatten).FirstOrDefault(n => n.Name == previous) ?? FolderTree[0];
            select.IsNodeSelected = true;
            SelectedFolder = select;
        }

        private static IEnumerable<FolderNode> Flatten(FolderNode node) =>
            new[] { node }.Concat(node.Children.SelectMany(Flatten));

        private void RefreshFolderCheckStates()
        {
            foreach (var node in FolderTree)
                node.RefreshCheckState();
        }

        // ── Thumbnail view ────────────────────────────────────────────────

        /// <summary>Set by the view from its width so rows fill the available space.</summary>
        public int ThumbnailColumns
        {
            get => _thumbnailColumns;
            set
            {
                value = Math.Max(1, value);
                if (_thumbnailColumns == value) return;
                _thumbnailColumns = value;
                _thumbnailsDirty  = true;
                ScheduleViewRefresh();
            }
        }

        private void RebuildThumbnails()
        {
            _thumbnailsDirty = false;

            var visible = _filesView.Cast<FileHashEntry>().ToList();
            var live    = new HashSet<FileHashEntry>(visible, ReferenceEqualityComparer.Instance);
            foreach (var stale in _tiles.Keys.Where(k => !live.Contains(k)).ToList())
                _tiles.Remove(stale);

            if (_tiles.Count == 0)
            {
                // New file list: abandon in-flight loads from the previous one.
                _thumbnailCts.Cancel();
                _thumbnailCts = new CancellationTokenSource();
            }

            ThumbnailRows.Clear();
            for (int i = 0; i < visible.Count; i += _thumbnailColumns)
            {
                var tiles = visible.Skip(i).Take(_thumbnailColumns).Select(f =>
                {
                    if (!_tiles.TryGetValue(f, out var tile)) _tiles[f] = tile = new ThumbnailTile(f);
                    return tile;
                }).ToList();
                ThumbnailRows.Add(new ThumbnailRow(tiles));
            }
        }

        /// <summary>Called by the view when a tile becomes visible.</summary>
        public async void RequestThumbnail(ThumbnailTile tile)
        {
            if (tile.LoadState != 0) return;
            tile.LoadState = 1;

            if (tile.Entry.IsDeviceSource && !_thumbnailReadsLogged)
            {
                _thumbnailReadsLogged = true;
                _custodyService.LogAction("Thumbnail Previews Read From Device (Read-Only)", _baseSourceFolder);
            }

            _thumbnailService ??= new ThumbnailService(OpenDeviceStream, path => _iosService.OpenRead(path));

            try
            {
                tile.Image = await _thumbnailService.LoadAsync(tile.Entry, _thumbnailCts.Token);
                tile.LoadState = 2;
            }
            catch (OperationCanceledException)
            {
                tile.LoadState = 0;
            }
        }
    }
}
