using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HashDash.Models
{
    /// <summary>
    /// A row in the Directory view: either a real folder on the source, or a virtual Photos
    /// grouping (album, app, media type). In virtual trees a file can sit under several
    /// nodes, so file enumeration is always de-duplicated.
    /// </summary>
    public class FolderNode : INotifyPropertyChanged
    {
        private readonly Dictionary<string, FolderNode> _childIndex = new(StringComparer.OrdinalIgnoreCase);
        private bool? _isChecked = true;
        private bool  _isExpanded;
        private bool  _isNodeSelected;

        public FolderNode(string name, string glyph)
        {
            Name  = name;
            Glyph = glyph;
        }

        public string Name  { get; }
        public string Glyph { get; }

        public ObservableCollection<FolderNode> Children { get; } = new();

        /// <summary>Files directly in this node (not in children).</summary>
        public List<FileHashEntry> Files { get; } = new();

        public int  TotalFiles { get; private set; }
        public long TotalBytes { get; private set; }

        public string TotalFilesDisplay => TotalFiles.ToString("N0");

        public string SizeDisplay
        {
            get
            {
                const double kb = 1024, mb = kb * 1024, gb = mb * 1024;
                return TotalBytes switch
                {
                    <= 0           => "0 KB",
                    < (long)mb     => $"{Math.Max(1, TotalBytes / kb):N0} KB",
                    < (long)gb     => $"{TotalBytes / mb:N2} MB",
                    _              => $"{TotalBytes / gb:N2} GB"
                };
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsNodeSelected
        {
            get => _isNodeSelected;
            set { _isNodeSelected = value; OnPropertyChanged(); }
        }

        /// <summary>Tri-state: all / none / some of the files beneath are selected for processing.</summary>
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                // A user click on an indeterminate box goes to null; treat that as "select all".
                bool select = value ?? true;
                foreach (var f in AllFiles())
                    f.IsSelected = select;
                SetCheckState(select);
            }
        }

        public FolderNode GetOrAddChild(string name, string glyph)
        {
            if (_childIndex.TryGetValue(name, out var child)) return child;
            child = new FolderNode(name, glyph);
            _childIndex[name] = child;
            Children.Add(child);
            return child;
        }

        /// <summary>Every file at or below this node, each once.</summary>
        public IEnumerable<FileHashEntry> AllFiles()
        {
            var seen  = new HashSet<FileHashEntry>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<FolderNode>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                foreach (var f in node.Files)
                    if (seen.Add(f)) yield return f;
                foreach (var c in node.Children)
                    stack.Push(c);
            }
        }

        /// <summary>Recomputes counts and sizes for this subtree; sorts children by name unless keepOrder.</summary>
        public void Finalise(bool keepOrder = false)
        {
            foreach (var c in Children) c.Finalise();

            if (!keepOrder && Children.Count > 1)
            {
                var sorted = Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
                Children.Clear();
                foreach (var c in sorted) Children.Add(c);
            }

            var all = AllFiles().ToList();
            TotalFiles = all.Count;
            TotalBytes = all.Sum(f => f.FileSizeBytes);
        }

        /// <summary>Recomputes tri-state checkboxes bottom-up from the files' IsSelected flags.</summary>
        public (bool anySelected, bool anyUnselected) RefreshCheckState()
        {
            bool any = false, none = false;
            foreach (var f in Files)
            {
                if (f.IsSelected) any = true; else none = true;
            }
            foreach (var c in Children)
            {
                var (a, n) = c.RefreshCheckState();
                any |= a;
                none |= n;
            }

            SetCheckState(any && none ? null : any || !none);
            return (any, none);
        }

        private void SetCheckState(bool? value)
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
