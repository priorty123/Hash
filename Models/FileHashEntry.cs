using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace HashDash.Models
{
    public class FileHashEntry : INotifyPropertyChanged
    {
        private int _id;
        private string _fileName = "";
        private string _fullPath = "";
        private long _fileSizeBytes;
        private bool _isMtpSource;
        private string? _mtpDeviceId;
        private string? _mtpDevicePath;
        private string _md5 = "";
        private string _sha256 = "";
        private string _fileDate = "";
        private string _verifyDate = "";
        private bool _isSelected = true;

        // Copy-and-verify fields
        private string _destPath = "";
        private string _destMd5 = "";
        private string _destSha256 = "";
        private string _copyStatus = "";
        private string _verificationResult = "";
        private string _transferDate = "";
        private string _errorDetail = "";

        // Secondary (backup) copy fields
        private string _secondaryDestPath   = "";
        private string _secondaryDestMd5    = "";
        private string _secondaryDestSha256 = "";

        // ── Identity ──────────────────────────────────────────────────────────

        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True when this file lives on an MTP-connected device (e.g. a phone or drone
        /// controller) rather than a real filesystem path. FullPath holds a synthesized
        /// display path in this case; MtpDeviceId/MtpDevicePath are the real handles
        /// used to read from the device.
        /// </summary>
        public bool IsMtpSource
        {
            get => _isMtpSource;
            set { _isMtpSource = value; OnPropertyChanged(); }
        }

        public string? MtpDeviceId
        {
            get => _mtpDeviceId;
            set { _mtpDeviceId = value; OnPropertyChanged(); }
        }

        /// <summary>Device-relative path used with MediaDevices' GetFileInfo/OpenRead.</summary>
        public string? MtpDevicePath
        {
            get => _mtpDevicePath;
            set { _mtpDevicePath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True when this file lives on an iOS device read over Apple File Conduit
        /// (IosAfcService). FullPath holds a synthesized display path; IosDevicePath is the
        /// media-root relative path used to read it.
        /// </summary>
        public bool IsIosSource { get; set; }

        public string? IosDevicePath { get; set; }

        /// <summary>True for any source without a real filesystem path (MTP or iOS).</summary>
        public bool IsDeviceSource => IsMtpSource || IsIosSource;

        /// <summary>
        /// Photos-app groupings this file belongs to, as "Section/Name" — e.g.
        /// "Albums/WhatsApp", "Saved by App/Snapchat", "Utilities/Hidden". iOS sources only.
        /// </summary>
        public IReadOnlyList<string> PhotoGroups { get; set; } = Array.Empty<string>();

        /// <summary>Album / app names for the grid and exports, e.g. "WhatsApp; Favourites".</summary>
        public string PhotoAlbumsDisplay =>
            string.Join("; ", PhotoGroups
                .Where(g => !g.StartsWith(Services.IosPhotoLibraryService.SectionMediaTypes + "/"))
                .Select(g => g.Substring(g.IndexOf('/') + 1)));

        /// <summary>The app that saved the item into the Photos library, when iOS recorded one.</summary>
        public string SavedByApp { get; set; } = "";

        /// <summary>How the file relates to the Photos library: Original, Live Photo video, Edited render, Thumbnail...</summary>
        public string LibraryRole { get; set; } = "";

        /// <summary>
        /// Raw byte count from FileInfo.Length — OS metadata read, zero file I/O.
        /// </summary>
        public long FileSizeBytes
        {
            get => _fileSizeBytes;
            set
            {
                if (_fileSizeBytes == value) return;
                _fileSizeBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeDisplay));
            }
        }

        /// <summary>
        /// Adaptive KB/MB/GB display matching Windows Explorer behaviour.
        /// </summary>
        public string SizeDisplay
        {
            get
            {
                long b = _fileSizeBytes;
                if (b <= 0) return "0 KB";

                const long kb = 1024;
                const long mb = 1024 * kb;
                const long gb = 1024 * mb;

                if (b < mb) return $"{System.Math.Max(1, (b + kb - 1) / kb):N0} KB";
                if (b < gb) return $"{b / (double)mb:N2} MB";
                return $"{b / (double)gb:N2} GB";
            }
        }

        // ── Source hash results ───────────────────────────────────────────────

        public string Md5
        {
            get => _md5;
            set { _md5 = value; OnPropertyChanged(); }
        }

        public string Sha256
        {
            get => _sha256;
            set { _sha256 = value; OnPropertyChanged(); }
        }

public DateTime FileDateValue { get; set; }

public string FileDate
{
    get => _fileDate;
    set { _fileDate = value; OnPropertyChanged(); }
}

        public string VerifyDate
        {
            get => _verifyDate;
            set { _verifyDate = value; OnPropertyChanged(); }
        }

        // ── Copy + verify results ─────────────────────────────────────────────

        public string DestPath
        {
            get => _destPath;
            set { _destPath = value; OnPropertyChanged(); }
        }

        public string DestMd5
        {
            get => _destMd5;
            set { _destMd5 = value; OnPropertyChanged(); }
        }

        public string DestSha256
        {
            get => _destSha256;
            set { _destSha256 = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Pending / Hashing Source / Copying / Hashing Dest / Verified / Mismatch / Failed / Cancelled
        /// </summary>
        public string CopyStatus
        {
            get => _copyStatus;
            set
            {
                _copyStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBrush));
            }
        }

        public string VerificationResult
        {
            get => _verificationResult;
            set { _verificationResult = value; OnPropertyChanged(); }
        }

public DateTime TransferDateValue { get; set; }

public string TransferDate
{
    get => _transferDate;
    set { _transferDate = value; OnPropertyChanged(); }
}

        public string ErrorDetail
        {
            get => _errorDetail;
            set { _errorDetail = value; OnPropertyChanged(); }
        }

        public string SecondaryDestPath
        {
            get => _secondaryDestPath;
            set { _secondaryDestPath = value; OnPropertyChanged(); }
        }

        public string SecondaryDestMd5
        {
            get => _secondaryDestMd5;
            set { _secondaryDestMd5 = value; OnPropertyChanged(); }
        }

        public string SecondaryDestSha256
        {
            get => _secondaryDestSha256;
            set { _secondaryDestSha256 = value; OnPropertyChanged(); }
        }

        // ── Derived ───────────────────────────────────────────────────────────

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Brush for the Status column — computed from CopyStatus.
        /// </summary>
       public Brush StatusBrush
{
    get
    {
        if (CopyStatus == null) return Brushes.Gray;
        
        // Green for verified
        if (CopyStatus.Contains("Verified", StringComparison.OrdinalIgnoreCase))
            return Brushes.Green;
        
        // Red for failures and mismatches
        if (CopyStatus.Contains("Mismatch", StringComparison.OrdinalIgnoreCase) ||
            CopyStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            CopyStatus.Contains("Error", StringComparison.OrdinalIgnoreCase))
            return Brushes.Red;
        
        // Orange for cancelled
        if (CopyStatus.Contains("Cancelled", StringComparison.OrdinalIgnoreCase))
            return Brushes.Orange;
        
        // Blue for in-progress
        if (CopyStatus.Contains("Hashing", StringComparison.OrdinalIgnoreCase) ||
            CopyStatus.Contains("Copying", StringComparison.OrdinalIgnoreCase) ||
            CopyStatus.Contains("Verifying", StringComparison.OrdinalIgnoreCase))
            return Brushes.DodgerBlue;
        
        // Default gray
        return Brushes.Gray;
    }
}

        // ── Resets ────────────────────────────────────────────────────────────

        /// <summary>
        /// Clears all hash and copy results so the entry can be re-processed.
        /// </summary>
        public void ResetResults()
        {
            Md5 = Sha256 = DestMd5 = DestSha256 = "";
            SecondaryDestMd5 = SecondaryDestSha256 = "";
            DestPath = SecondaryDestPath = VerificationResult = TransferDate = ErrorDetail = "";
            CopyStatus = "";
            VerifyDate = "";
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}