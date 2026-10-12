using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace HashDash.Models
{
    /// <summary>One tile in the Thumbnail view. The image loads lazily when the tile scrolls into view.</summary>
    public class ThumbnailTile : INotifyPropertyChanged
    {
        private ImageSource? _image;

        public ThumbnailTile(FileHashEntry entry)
        {
            Entry = entry;
            Glyph = GlyphFor(Path.GetExtension(entry.FileName));
        }

        public FileHashEntry Entry { get; }

        /// <summary>Segoe Fluent glyph shown until (or instead of) a thumbnail.</summary>
        public string Glyph { get; }

        /// <summary>0 = not requested, 1 = loading, 2 = done (image may still be null if none could be made).</summary>
        public int LoadState { get; set; }

        public ImageSource? Image
        {
            get => _image;
            set { _image = value; OnPropertyChanged(); }
        }

        public static bool IsImage(string extension) => ImageExtensions.Contains(extension.ToLowerInvariant());
        public static bool IsVideo(string extension) => VideoExtensions.Contains(extension.ToLowerInvariant());

        private static readonly HashSet<string> ImageExtensions = new()
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".webp", ".dng"
        };

        private static readonly HashSet<string> VideoExtensions = new()
        {
            ".mov", ".mp4", ".m4v", ".avi", ".3gp", ".mkv", ".wmv"
        };

        private static string GlyphFor(string extension)
        {
            string ext = extension.ToLowerInvariant();
            if (IsImage(ext)) return "";                       // Photo
            if (IsVideo(ext)) return "";                       // Video
            return ext switch
            {
                ".mp3" or ".m4a" or ".wav" or ".aac" or ".caf" or ".amr" => "", // Audio
                ".sqlite" or ".db" or ".plist" or ".sqlite-wal" or ".sqlite-shm" => "", // Database-ish
                _ => ""                                        // Page
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>A row of tiles. Rows are what the Thumbnail view virtualises, so only visible rows exist as UI.</summary>
    public class ThumbnailRow
    {
        public ThumbnailRow(IReadOnlyList<ThumbnailTile> tiles) => Tiles = tiles;
        public IReadOnlyList<ThumbnailTile> Tiles { get; }
    }
}
