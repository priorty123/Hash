using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HashDash.Commands;
using HashDash.Helpers;
using HashDash.Models;
using HashDash.Services;
using Microsoft.Win32;

namespace HashDash.ViewModels
{
    public partial class MainViewModel : BaseViewModel, IDataErrorInfo
    {
        private const string AppName    = "HashDash";
        private const string AppVersion = "2.2";
        private const int    PdfRowsPerFile     = 1_000;
        private const int    CsvRowsPerFile     = 100_000;

        // ── Services ──────────────────────────────────────────────────────
        private readonly HashService               _hashService;
        private readonly FileCopyService           _fileCopyService;
        private readonly ExportService             _exportService;
        private readonly FileNamingService         _fileNamingService;
        private readonly SettingsService           _settingsService;
        private readonly FileIntegrityService      _integrityService;
        private readonly DuplicateDetectionService _duplicateService;
        private readonly ChainOfCustodyService     _custodyService;
        private readonly DatabaseService           _databaseService;
        private readonly PdfReportService          _pdfReportService;
        private readonly FileMonitorService        _monitorService;
        private readonly DetailedLogService        _detailedLogService;
        private readonly NotificationService       _notificationService;
        private readonly ToastNotificationService  _toastService;
        private readonly MtpDeviceService          _mtpDeviceService;
        private readonly IosAfcService             _iosService;

        // ── State ─────────────────────────────────────────────────────────
        private CancellationTokenSource? _cts;
        private readonly DispatcherTimer _clockTimer;
        private string   _baseSourceFolder = "";
        private bool     _sourceIsMtp;
        private string?  _sourceMtpDeviceId;
        private string   _sourceMtpFriendlyName = "";
        private bool     _sourceIsIos;
        private int      _total;
        private int      _current;
        private DateTime _processStartTime;
        private string   _sessionId = "";
        private bool     _isProcessing;
        private bool     _hasAttemptedRun;
        private volatile bool _fileProgressActive;

        // ── Observable collections ────────────────────────────────────────
        public ObservableCollection<FileHashEntry> Files { get; }
        private readonly HashSet<string> _filePathSet = new(StringComparer.OrdinalIgnoreCase);

        // ── Properties ────────────────────────────────────────────────────

        private bool   _isUiEnabled     = true;
        private bool   _isCancelEnabled;
        private bool   _isCloseEnabled  = true;
        private string _sourcePath      = "";
        private string _destinationPath = "";
        private string _secondaryDestinationPath = "";
        private string _sourceDriveLabel             = "";
        private string _destinationDriveLabel        = "";
        private string _secondaryDestinationDriveLabel = "";
        private string _currentDateTime  = "";
        private string _currentFileText  = "";
        private double _progressValue;
        private double _currentFileProgress;
        private bool?  _isAllSelected;
        private string _examinerName = "";
        private string _labCaseRef   = "";
        private string _exhibitRef   = "";
        private bool   _md5Enabled    = true;
        private bool   _sha256Enabled = true;
        private bool   _automationEnabled;
        private string _completionMessage      = "";
        private string _triggerFilePattern     = "";
        private string _automationStatusMessage = "";
        private int    _unreadNotificationCount;

        public int UnreadNotificationCount
        {
            get => _unreadNotificationCount;
            set => SetProperty(ref _unreadNotificationCount, value);
        }

        private readonly ICollectionView _filesView;
        public ICollectionView FilesView => _filesView;

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) OnFilterChanged(); }
        }

        private string _statusFilter = "All";
        public string StatusFilter
        {
            get => _statusFilter;
            set { if (SetProperty(ref _statusFilter, value)) OnFilterChanged(); }
        }

        public static IReadOnlyList<string> StatusFilterOptions { get; } = new[]
        {
            "All", "Verified", "Failed / Mismatch", "Hash Complete", "Cancelled", "Pending"
        };

        public ObservableCollection<NotificationItem> Notifications => _notificationService.Notifications;

        public string TriggerFilePattern
        {
            get => _triggerFilePattern;
            set => SetProperty(ref _triggerFilePattern, value);
        }

        public string AutomationStatusMessage
        {
            get => _automationStatusMessage;
            set
            {
                if (SetProperty(ref _automationStatusMessage, value))
                {
                    OnPropertyChanged(nameof(ShowAutomationBanner));
                    OnPropertyChanged(nameof(AutomationStatusText));
                }
            }
        }

        public bool ShowAutomationBanner =>
            _automationEnabled || !string.IsNullOrEmpty(_automationStatusMessage);

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public bool IsUiEnabled
        {
            get => _isUiEnabled;
            set
            {
                if (SetProperty(ref _isUiEnabled, value))
                {
                    OnPropertyChanged(nameof(IsClearEnabled));
                    OnPropertyChanged(nameof(CanToggleAutomation));
                }
            }
        }

        public bool IsCancelEnabled
        {
            get => _isCancelEnabled;
            set => SetProperty(ref _isCancelEnabled, value);
        }

        public bool IsCloseEnabled
        {
            get => _isCloseEnabled;
            set => SetProperty(ref _isCloseEnabled, value);
        }

        public bool IsClearEnabled => IsUiEnabled && Files.Count > 0;

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public double CurrentFileProgress
        {
            get => _currentFileProgress;
            set => SetProperty(ref _currentFileProgress, value);
        }

        public string ProgressText =>
            _total == 0
                ? "Ready"
                : $"Overall: {_current} / {_total} files  ({100.0 * _current / _total:N1}%)";

        public string CurrentFileText
        {
            get => _currentFileText;
            set => SetProperty(ref _currentFileText, value);
        }

        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                if (SetProperty(ref _sourcePath, value))
                {
                    UpdateSourceDriveLabel();
                    _settingsService.AddRecentPath(value, true);
                    OnPropertyChanged(nameof(CanToggleAutomation));
                }
            }
        }

        public string DestinationPath
        {
            get => _destinationPath;
            set
            {
                if (SetProperty(ref _destinationPath, value))
                {
                    UpdateDestinationDriveLabel();
                    if (!string.IsNullOrEmpty(value))
                        _settingsService.AddRecentPath(value, false);
                }
            }
        }

        public string SecondaryDestinationPath
        {
            get => _secondaryDestinationPath;
            set
            {
                if (SetProperty(ref _secondaryDestinationPath, value))
                    UpdateSecondaryDestinationDriveLabel();
            }
        }

        public string SourceDriveLabel
        {
            get => _sourceDriveLabel;
            set => SetProperty(ref _sourceDriveLabel, value);
        }

        public string DestinationDriveLabel
        {
            get => _destinationDriveLabel;
            set => SetProperty(ref _destinationDriveLabel, value);
        }

        public string SecondaryDestinationDriveLabel
        {
            get => _secondaryDestinationDriveLabel;
            set => SetProperty(ref _secondaryDestinationDriveLabel, value);
        }

        public string ComputerName { get; }

        public string CurrentDateTime
        {
            get => _currentDateTime;
            set => SetProperty(ref _currentDateTime, value);
        }

        public bool? IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (SetProperty(ref _isAllSelected, value) && value.HasValue)
                {
                    foreach (var f in Files)
                        f.IsSelected = value.Value;
                }
            }
        }

        public string ExaminerName
        {
            get => _examinerName;
            set
            {
                if (SetProperty(ref _examinerName, value))
                    OnPropertyChanged(nameof(CanToggleAutomation));
            }
        }

        public string LabCaseRef
        {
            get => _labCaseRef;
            set
            {
                if (SetProperty(ref _labCaseRef, value))
                    OnPropertyChanged(nameof(CanToggleAutomation));
            }
        }

        public string ExhibitRef
        {
            get => _exhibitRef;
            set
            {
                if (SetProperty(ref _exhibitRef, value))
                    OnPropertyChanged(nameof(CanToggleAutomation));
            }
        }

        public bool Md5Enabled
        {
            get => _md5Enabled;
            set => SetProperty(ref _md5Enabled, value);
        }

        public bool Sha256Enabled
        {
            get => _sha256Enabled;
            set => SetProperty(ref _sha256Enabled, value);
        }

        public bool AutomationEnabled
        {
            get => _automationEnabled;
            set
            {
                if (SetProperty(ref _automationEnabled, value))
                {
                    OnPropertyChanged(nameof(CanToggleAutomation));
                    OnPropertyChanged(nameof(AreFieldsLocked));
                    OnPropertyChanged(nameof(AutomationStatusText));
                    OnPropertyChanged(nameof(ShowAutomationBanner));
                }
            }
        }

        public bool AreFieldsLocked
        {
            get => _automationEnabled;
            set { if (_automationEnabled != value) AutomationEnabled = value; }
        }

        public string AutomationStatusText =>
            !string.IsNullOrEmpty(_automationStatusMessage)
                ? _automationStatusMessage
                : _automationEnabled
                    ? "AUTOMATION ACTIVE - Monitoring..."
                    : "Normal Mode";

        public bool CanToggleAutomation =>
            !string.IsNullOrWhiteSpace(_examinerName) &&
            !string.IsNullOrWhiteSpace(_labCaseRef)   &&
            !string.IsNullOrWhiteSpace(_exhibitRef)   &&
            !string.IsNullOrWhiteSpace(_sourcePath)   &&
            !_isProcessing;

        public string CompletionMessage
        {
            get => _completionMessage;
            set => SetProperty(ref _completionMessage, value);
        }

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand DismissAutomationMessageCommand          { get; }
        public ICommand BrowseFolderCommand                    { get; }
        public ICommand ConnectMtpDeviceCommand                { get; }
        public ICommand BrowseDestinationCommand               { get; }
        public ICommand BrowseSecondaryDestinationCommand      { get; }
        public ICommand RunCommand               { get; }
        public ICommand CancelCommand            { get; }
        public ICommand ClearCommand             { get; }
        public ICommand ExportCsvCommand         { get; }
        public ICommand ExportPdfCommand         { get; }
        public ICommand ScreenshotCommand        { get; }
        public ICommand CloseCommand             { get; }
        public ICommand ViewLogsCommand          { get; }
        public ICommand DetectDuplicatesCommand  { get; }
        public ICommand ToggleAutomationCommand  { get; }
        public ICommand OpenSettingsCommand      { get; }
        public ICommand OpenLogsWindowCommand    { get; }
        public ICommand ExportDetailedLogCommand { get; }
        public ICommand ShowNotificationsCommand  { get; }
        public ICommand ClearNotificationsCommand { get; }

        // ── Constructor ───────────────────────────────────────────────────

        public MainViewModel()
        {
            _hashService        = new HashService();
            _fileCopyService    = new FileCopyService();
            _exportService      = new ExportService();
            _fileNamingService  = new FileNamingService();
            _settingsService    = new SettingsService();
            _integrityService   = new FileIntegrityService();
            _duplicateService   = new DuplicateDetectionService();
            _custodyService     = new ChainOfCustodyService();
            _databaseService    = new DatabaseService();
            _pdfReportService   = new PdfReportService();
            _monitorService     = new FileMonitorService(new AutomationConfig());
            _detailedLogService = new DetailedLogService();
            _mtpDeviceService   = new MtpDeviceService();
            _iosService         = new IosAfcService();

            _notificationService = new NotificationService();
            _notificationService.NotificationAdded += (_, _) =>
                UnreadNotificationCount = _notificationService.UnreadCount;

            _toastService = new ToastNotificationService(_notificationService);

            var settings = _settingsService.Settings;
            Md5Enabled         = settings.Md5Enabled;
            Sha256Enabled      = settings.Sha256Enabled;
            TriggerFilePattern = settings.TriggerFilePattern;

            Files = new ObservableCollection<FileHashEntry>();

            _filesView = CollectionViewSource.GetDefaultView(Files);
            _filesView.Filter = FilterFiles;

            ComputerName = Environment.MachineName;

            InitializeDatabase();

            DismissAutomationMessageCommand       = new RelayCommand(DismissAutomationMessage);
            BrowseFolderCommand                   = new RelayCommand(BrowseFolder,               () => !AreFieldsLocked);
            ConnectMtpDeviceCommand                = new RelayCommand(ConnectMtpDevice,           () => !AreFieldsLocked && !_isProcessing);
            BrowseDestinationCommand              = new RelayCommand(BrowseDestination,           () => !AreFieldsLocked);
            BrowseSecondaryDestinationCommand     = new RelayCommand(BrowseSecondaryDestination, () => !AreFieldsLocked);
            RunCommand               = new RelayCommand(ExecuteRun,         () => !AreFieldsLocked && !_isProcessing);
            CancelCommand            = new RelayCommand(Cancel,             () => IsCancelEnabled);
            ClearCommand             = new RelayCommand(Clear,              () => IsClearEnabled && !AreFieldsLocked);
            ExportCsvCommand         = new RelayCommand(ExportCsv,          () => IsUiEnabled && Files.Count > 0);
            ExportPdfCommand         = new RelayCommand(ExportPdf,          () => IsUiEnabled && Files.Count > 0);
            ScreenshotCommand        = new RelayCommand(CaptureScreenshot,  () => IsUiEnabled);
            CloseCommand             = new RelayCommand(Close,              () => IsCloseEnabled);
            ViewLogsCommand          = new RelayCommand(ViewLogs);
            DetectDuplicatesCommand  = new RelayCommand(DetectDuplicates,   () => Files.Count > 0);
            ToggleAutomationCommand  = new RelayCommand(ToggleAutomation,   () => CanToggleAutomation || _automationEnabled);
            OpenSettingsCommand      = new RelayCommand(OpenSettings);
            OpenLogsWindowCommand    = new RelayCommand(OpenLogsWindow);
            ExportDetailedLogCommand = new RelayCommand(ExportDetailedLog,  () => IsUiEnabled && !string.IsNullOrEmpty(_detailedLogService.GetLogContent()));
            ShowNotificationsCommand  = new RelayCommand(ShowNotifications);
            ClearNotificationsCommand = new RelayCommand(ClearNotifications);

            Files.CollectionChanged += (_, _) =>
            {
                UpdateSelectAllState();
                OnPropertyChanged(nameof(IsClearEnabled));
                MarkViewsDirty();
            };

            _monitorService.FolderDetected += OnFolderDetected;

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();
        }

        // ── Database initialisation ───────────────────────────────────────

        private void InitializeDatabase()
        {
            try
            {
                string dbPath = _settingsService.Settings.DatabasePath;
                if (string.IsNullOrWhiteSpace(dbPath))
                {
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(folder);
                    dbPath = Path.Combine(folder, "ProcessLog.db");
                    _settingsService.Settings.DatabasePath = dbPath;
                    _settingsService.SaveSettings();
                }
                _databaseService.Initialize(dbPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initialising database: {ex.Message}\n\nPlease configure database path in Settings.",
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── UI helpers ────────────────────────────────────────────────────

        private void UpdateClock() =>
            CurrentDateTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        private void UpdateSourceDriveLabel() =>
            SourceDriveLabel = _sourceIsIos ? $"(iOS: {_sourceMtpFriendlyName})"
                             : _sourceIsMtp ? $"(MTP: {_sourceMtpFriendlyName})"
                             : DriveInfoHelper.FormatDriveLabelForDisplay(_sourcePath, ComputerName);

        private void UpdateDestinationDriveLabel() =>
            DestinationDriveLabel = DriveInfoHelper.FormatDriveLabelForDisplay(_destinationPath, ComputerName);

        private void UpdateSecondaryDestinationDriveLabel() =>
            SecondaryDestinationDriveLabel = DriveInfoHelper.FormatDriveLabelForDisplay(_secondaryDestinationPath, ComputerName);

        private void DismissAutomationMessage() =>
            AutomationStatusMessage = "";

        private bool _selectionRefreshPending;

        /// <summary>
        /// Coalesces selection-state refreshes: toggling a folder or album flips thousands of
        /// IsSelected flags, and recomputing the header and folder-tree checkboxes once per
        /// flag would be quadratic.
        /// </summary>
        private void UpdateSelectAllState()
        {
            if (_selectionRefreshPending) return;
            _selectionRefreshPending = true;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _selectionRefreshPending = false;

                bool? next = Files.Count == 0              ? false
                           : Files.All(f => f.IsSelected)  ? true
                           : Files.All(f => !f.IsSelected) ? false
                           : (bool?)null;

                if (_isAllSelected != next)
                {
                    _isAllSelected = next;
                    OnPropertyChanged(nameof(IsAllSelected));
                }

                RefreshFolderCheckStates();
            }, DispatcherPriority.Background);
        }

        // ── Browse commands ───────────────────────────────────────────────

        private async void BrowseFolder()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    CheckFileExists = false,
                    FileName = "Select Source Folder (Network paths supported)",
                    Title    = "Select Source Folder"
                };

                if (dlg.ShowDialog() != true) return;

                string? folder = Path.GetDirectoryName(dlg.FileName);
                if (string.IsNullOrEmpty(folder))
                {
                    MessageBox.Show("Invalid folder path.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(folder))
                {
                    MessageBox.Show(
                        $"The selected folder does not exist or is not accessible:\n{folder}\n\n" +
                        "Please check the path and your permissions.",
                        "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _baseSourceFolder      = folder;
                _sourceIsMtp           = false;
                _sourceIsIos           = false;
                _sourceMtpDeviceId     = null;
                _sourceMtpFriendlyName = "";
                _mtpDeviceService.Disconnect();
                _iosService.Disconnect();
                _custodyService.LogAction("Source Folder Selected", folder);
                Files.Clear();
                _filePathSet.Clear();

                try
                {
                    await LoadFilesAsync(folder);

                    SourcePath = folder;
                    _toastService.ShowSuccess("Files Loaded",
                        $"Loaded {Files.Count} files from {(folder.StartsWith(@"\\") ? "network" : "local")} location");

                    if (Files.Count > PdfRowsPerFile)
                        _toastService.ShowWarning("Large Dataset",
                            $"{Files.Count:N0} files loaded. PDF export will be split into multiple files " +
                            $"of up to {PdfRowsPerFile:N0} rows each.");
                }
                catch (UnauthorizedAccessException)
                {
                    _toastService.ShowError("Access Denied",
                        $"Cannot access folder. Check permissions: {Path.GetFileName(folder)}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ConnectMtpDevice()
        {
            if (_isProcessing)
            {
                MessageBox.Show("Please wait for the current operation to finish.", "Busy",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new MtpDevicePickerWindow { Owner = Application.Current.MainWindow };
            if (picker.ShowDialog() != true || picker.SelectedDevice == null) return;

            if (picker.SelectedDevice.Ios != null)
            {
                await ConnectIosDeviceAsync(picker.SelectedDevice.Ios, picker.ScanEntireMediaPartition);
                return;
            }

            var device = picker.SelectedDevice.Mtp!;

            Files.Clear();
            _filePathSet.Clear();
            _iosService.Disconnect();
            _sourceIsIos = false;

            using var cts = new CancellationTokenSource();
            _cts = cts;
            SetOperationState(true);
            CurrentFileText = $"Connecting to {device.FriendlyName}...";

            try
            {
                await LoadFilesFromMtpDeviceAsync(device.DeviceId, device.FriendlyName, cts.Token);

                _baseSourceFolder      = $"[{device.FriendlyName}]";
                _sourceIsMtp           = true;
                _sourceMtpDeviceId     = device.DeviceId;
                _sourceMtpFriendlyName = device.FriendlyName;
                SourcePath             = $"MTP: {device.FriendlyName}";

                _custodyService.LogAction(
                    "MTP Device Connected — Read-Only Session Requested",
                    device.FriendlyName, "", $"DeviceId: {device.DeviceId}");

                _toastService.ShowSuccess("Device Connected",
                    $"Loaded {Files.Count} files from {device.FriendlyName}");
            }
            catch (OperationCanceledException)
            {
                _toastService.ShowWarning("Cancelled",
                    $"Device scan cancelled: {device.FriendlyName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading from device: {ex.Message}", "Device Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cts = null;
                SetOperationState(false);
            }
        }

        private async Task LoadFilesFromMtpDeviceAsync(string deviceId, string friendlyName, CancellationToken cancellationToken)
        {
            var batch = new System.Collections.Generic.List<FileHashEntry>(500);
            string displayRoot = $"[{friendlyName}]";

            CurrentFileText = $"Scanning {friendlyName} for files...";

            var progress = new Progress<int>(count =>
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CurrentFileText = $"Scanning {friendlyName}... {count:N0} files found";
                }, DispatcherPriority.Normal);
            });

            var skipped = new System.Collections.Generic.List<string>();
            var entries = await Task.Run(
                () => _mtpDeviceService.EnumerateFilesRecursive(deviceId, cancellationToken, progress, skipped),
                cancellationToken);

            foreach (string folder in skipped)
                _custodyService.LogAction("MTP Folder Refused By Device", $"{displayRoot}\\{folder}");
            if (skipped.Count > 0)
                _toastService.ShowWarning("Folders Refused",
                    $"The device refused to list {skipped.Count} folder(s): {string.Join(", ", skipped.Take(5))}" +
                    (skipped.Count > 5 ? ", ..." : ""));

            CurrentFileText = $"Loading {entries.Count:N0} files into the list...";

            foreach (var entry in entries)
            {
                string displayPath = Path.Combine(displayRoot, entry.RelativePath);
                if (!_filePathSet.Add(displayPath)) continue;

                var hashEntry = new FileHashEntry
                {
                    FileName      = Path.GetFileName(entry.RelativePath),
                    FullPath      = displayPath,
                    FileDate      = entry.LastWriteTime.ToString("g"),
                    FileDateValue = entry.LastWriteTime,
                    FileSizeBytes = entry.Length,
                    IsSelected    = true,
                    IsMtpSource   = true,
                    MtpDeviceId   = deviceId,
                    MtpDevicePath = entry.DevicePath
                };

                hashEntry.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FileHashEntry.IsSelected))
                        UpdateSelectAllState();
                };

                batch.Add(hashEntry);

                if (batch.Count >= 500)
                {
                    var toAdd = new System.Collections.Generic.List<FileHashEntry>(batch);
                    batch.Clear();
                    foreach (var e in toAdd) Files.Add(e);
                }
            }

            foreach (var e in batch) Files.Add(e);
        }

        private void BrowseDestination()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    CheckFileExists = false,
                    FileName = "Select Destination Folder (Network paths supported)",
                    Title    = "Select Destination Folder"
                };

                if (dlg.ShowDialog() != true) return;

                string? folder = Path.GetDirectoryName(dlg.FileName);
                if (string.IsNullOrEmpty(folder))
                {
                    MessageBox.Show("Invalid destination path.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(folder))
                {
                    MessageBox.Show(
                        $"The selected folder does not exist or is not accessible:\n{folder}\n\n" +
                        "Please check the path and your permissions.",
                        "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    string testFile = Path.Combine(folder, $"_hashDash_write_test_{Guid.NewGuid()}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Cannot write to destination folder:\n{folder}\n\n{ex.Message}\n\n" +
                        "Please check your network permissions.",
                        "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                DestinationPath = folder;
                _custodyService.LogAction("Destination Folder Selected", folder);
                _toastService.ShowSuccess("Destination Selected",
                    $"{(folder.StartsWith(@"\\") ? "Network" : "Local")} folder: {Path.GetFileName(folder)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting destination: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseSecondaryDestination()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    CheckFileExists = false,
                    FileName = "Select Backup Destination Folder (Network paths supported)",
                    Title    = "Select Backup Destination Folder"
                };

                if (dlg.ShowDialog() != true) return;

                string? folder = Path.GetDirectoryName(dlg.FileName);
                if (string.IsNullOrEmpty(folder))
                {
                    MessageBox.Show("Invalid backup destination path.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(folder))
                {
                    MessageBox.Show(
                        $"The selected folder does not exist or is not accessible:\n{folder}\n\n" +
                        "Please check the path and your permissions.",
                        "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    string testFile = Path.Combine(folder, $"_hashDash_write_test_{Guid.NewGuid()}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Cannot write to backup destination folder:\n{folder}\n\n{ex.Message}\n\n" +
                        "Please check your network permissions.",
                        "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SecondaryDestinationPath = folder;
                _custodyService.LogAction("Secondary Destination Folder Selected", folder);
                _toastService.ShowSuccess("Secondary Destination Selected",
                    $"{(folder.StartsWith(@"\\") ? "Network" : "Local")} folder: {Path.GetFileName(folder)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting secondary destination: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!_filePathSet.Add(path)) return;

                var fi = new FileInfo(path);
                if (!fi.Exists) return;

                var entry = new FileHashEntry
                {
                    Id            = Files.Count + 1,
                    FileName      = fi.Name,
                    FullPath      = fi.FullName,
                   FileDate      = fi.LastWriteTime.ToString("g"),
FileDateValue = fi.LastWriteTime,
                    FileSizeBytes = fi.Length,
                    IsSelected    = true
                };

                entry.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FileHashEntry.IsSelected))
                        UpdateSelectAllState();
                };

                _integrityService.CaptureMetadata(path);
                _custodyService.LogAction("File Added", path, "", $"Size: {fi.Length} bytes");
                Files.Add(entry);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding file {path}: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadFilesAsync(string folder)
        {
            var batch = new System.Collections.Generic.List<FileHashEntry>(500);

            await Task.Run(() =>
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!_filePathSet.Add(path)) continue;

                    try
                    {
                        var fi = new FileInfo(path);
                        if (!fi.Exists) continue;

                        var entry = new FileHashEntry
                        {
                            FileName      = fi.Name,
                            FullPath      = fi.FullName,
                            FileDate      = fi.LastWriteTime.ToString("g"),
                            FileDateValue = fi.LastWriteTime,
                            FileSizeBytes = fi.Length,
                            IsSelected    = true
                        };

                        entry.PropertyChanged += (_, args) =>
                        {
                            if (args.PropertyName == nameof(FileHashEntry.IsSelected))
                                UpdateSelectAllState();
                        };

                        batch.Add(entry);

                        if (batch.Count >= 500)
                        {
                            var toAdd = new System.Collections.Generic.List<FileHashEntry>(batch);
                            batch.Clear();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                foreach (var e in toAdd)
                                {
                                    e.Id = Files.Count + 1;
                                    Files.Add(e);
                                }
                            });
                        }
                    }
                    catch { /* skip inaccessible files */ }
                }
            });

            // flush remaining
            if (batch.Count > 0)
            {
                foreach (var e in batch)
                {
                    e.Id = Files.Count + 1;
                    Files.Add(e);
                }
            }

            _custodyService.LogAction("Source Folder Loaded", folder, "",
                $"{Files.Count} files enumerated");
        }

        // ── Run execution ─────────────────────────────────────────────────

        private async void ExecuteRun()
        {
            _hasAttemptedRun = true;
            OnPropertyChanged(nameof(ExaminerName));
            OnPropertyChanged(nameof(LabCaseRef));
            OnPropertyChanged(nameof(ExhibitRef));

            if (string.IsNullOrWhiteSpace(_examinerName) ||
                string.IsNullOrWhiteSpace(_labCaseRef)   ||
                string.IsNullOrWhiteSpace(_exhibitRef))
                return;

            if (!ValidateAlgorithms())    return;
            if (!ValidateFileSelection()) return;

            if (string.IsNullOrWhiteSpace(_sourcePath))
            {
                MessageBox.Show("Please select a source folder.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasDestination = !string.IsNullOrWhiteSpace(_destinationPath);
            bool hasSecondary   = !string.IsNullOrWhiteSpace(_secondaryDestinationPath);

            if (hasSecondary && !hasDestination)
            {
                MessageBox.Show(
                    "A primary destination path is required when a backup destination is specified.\n\n" +
                    "Please select a primary destination folder or clear the backup destination.",
                    "Primary Destination Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (hasDestination && !Directory.Exists(_destinationPath))
            {
                MessageBox.Show("The selected destination folder no longer exists.",
                    "Destination Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (hasSecondary && !Directory.Exists(_secondaryDestinationPath))
            {
                MessageBox.Show("The selected backup destination folder no longer exists.",
                    "Backup Destination Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (hasDestination && !CheckDiskSpace()) return;

            _processStartTime = DateTime.Now;
            _sessionId = GenerateSessionId();

            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                _detailedLogService.StartNewLog(logDirectory, _exhibitRef, _labCaseRef);
                _detailedLogService.LogEvent("PROCESS", "Process started",
                    $"Mode: {(hasDestination ? "Copy & Verify" : "Hash Only")}");
                _detailedLogService.LogEvent("CONFIG", "Source", _sourcePath);
                if (hasDestination) _detailedLogService.LogEvent("CONFIG", "Destination", _destinationPath);
                if (hasSecondary)  _detailedLogService.LogEvent("CONFIG", "Backup Destination", _secondaryDestinationPath);
                _detailedLogService.LogEvent("CONFIG", "Algorithms",
                    $"MD5: {_md5Enabled}, SHA-256: {_sha256Enabled}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting detailed log: {ex.Message}");
            }

            using var cts = new CancellationTokenSource();
            _cts = cts;
            SetOperationState(true);

            try
            {
                if (hasDestination)
                    await CopyAndVerifyAsync(cts.Token);
                else
                    await HashOnlyAsync(cts.Token);

                var selected       = Files.Where(f => f.IsSelected).ToList();
                int totalProcessed = selected.Count;
                int successful     = selected.Count(f =>
                    f.CopyStatus is "Verified" or "Verified (Existing)"
                                 or "Verified (Primary + Secondary)" or "Verified (Primary + Secondary Existing)"
                                 or "Hash Complete");
                int failed = selected.Count(f =>
                    f.CopyStatus.Contains("Failed",   StringComparison.OrdinalIgnoreCase) ||
                    f.CopyStatus.Contains("Mismatch", StringComparison.OrdinalIgnoreCase) ||
                    f.VerificationResult == "MISMATCH");

                TimeSpan totalDuration = DateTime.Now - _processStartTime;
                _detailedLogService.LogSummary(totalProcessed, successful, failed, totalDuration);

                await LogProcessToDatabase("Success", null);
                await GenerateAutomatedPdfReport();

                var modified = _integrityService.GetIntegrityReport()
                    .Where(kvp => !kvp.Value).ToList();
                if (modified.Any())
                    MessageBox.Show($"WARNING: {modified.Count} file(s) were modified during processing!",
                        "Integrity Alert", MessageBoxButton.OK, MessageBoxImage.Warning);

                string mode = hasDestination ? "Copy and verification" : "Hashing";
                CompletionMessage =
                    $"Process completed at {DateTime.Now:HH:mm:ss}\n" +
                    $"Files processed: {totalProcessed}\n" +
                    $"Successful: {successful}\n" +
                    (failed > 0 ? $"Failed: {failed}\n" : "") +
                    $"Duration: {totalDuration.Hours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";

                if (!_automationEnabled)
                {
                    string resultMessage = failed > 0
                        ? $"{mode} completed with {failed} failure(s).\n\n" +
                          $"Total files: {totalProcessed}\nSuccessful: {successful}\nFailed: {failed}\n\n" +
                          $"Duration: {totalDuration.Hours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}\n\nLog and PDF report saved."
                        : $"{mode} completed successfully!\n\n" +
                          $"Total files: {totalProcessed}\nAll files verified\n\n" +
                          $"Duration: {totalDuration.Hours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}\n\nLog and PDF report saved.";

                    MessageBox.Show(resultMessage, "Operation Complete", MessageBoxButton.OK,
                        failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                else
                {
                    _toastService.ShowAutomationComplete(totalProcessed, successful, failed, totalDuration);
                    StopMonitoring();
                    AutomationEnabled = false;
                    AutomationStatusMessage = failed > 0
                        ? $"AUTOMATION COMPLETED WITH WARNINGS - {successful}/{totalProcessed} successful, {failed} failed"
                        : $"AUTOMATION COMPLETED SUCCESSFULLY - {totalProcessed} files processed";
                }
            }
            catch (OperationCanceledException)
            {
                _detailedLogService.LogEvent("SYSTEM", "Operation cancelled by user");
                await LogProcessToDatabase("Cancelled", null);
                CompletionMessage = "Operation cancelled";

                if (_automationEnabled)
                {
                    StopMonitoring();
                    AutomationEnabled = false;
                    AutomationStatusMessage = "AUTOMATION CANCELLED - Operation was cancelled by user";
                    _toastService.ShowWarning("Automation Cancelled", "The automation task was cancelled.");
                }
                else
                {
                    MessageBox.Show("Operation cancelled.", "Cancelled",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _detailedLogService.LogEvent("ERROR", "Critical error occurred", ex.Message);
                await LogProcessToDatabase("Failed", ex.Message);
                CompletionMessage = $"Operation failed: {ex.Message}";

                if (_automationEnabled)
                {
                    StopMonitoring();
                    AutomationEnabled = false;
                    AutomationStatusMessage = $"AUTOMATION FAILED - Error: {ex.Message}";
                    _toastService.ShowError("Automation Failed", $"Error: {ex.Message}");
                }
                else
                {
                    MessageBox.Show($"Error during operation: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _cts = null;
                SetOperationState(false);
            }
        }

        // ── Hash only mode ────────────────────────────────────────────────

        private async Task HashOnlyAsync(CancellationToken token)
        {
            var list = Files.Where(f => f.IsSelected).ToList();
            _total   = list.Count;
            _current = 0;

            _detailedLogService.LogEvent("PROCESS", $"Starting hash-only mode for {_total} files");

            foreach (var f in list)
            {
                DateTime fileStartTime = DateTime.Now;
                _detailedLogService.LogFileStart(f.FileName, _current + 1, _total);

                token.ThrowIfCancellationRequested();
                _current++;
                OnPropertyChanged(nameof(ProgressText));
                f.ResetResults();

                CurrentFileText     = $"Hashing: {f.FileName}";
                CurrentFileProgress = 0;
                f.CopyStatus        = "Hashing... 0%";
                _fileProgressActive = true;

                DateTime hashStart = DateTime.Now;
                try
                {
                    var hashProgress = new Progress<HashProgressEventArgs>(args =>
                    {
                       Application.Current.Dispatcher.InvokeAsync(() =>
{
    if (!_fileProgressActive) return;
    CurrentFileProgress = args.PercentComplete;
    f.CopyStatus        = $"Hashing... {(int)args.PercentComplete}%";
}, DispatcherPriority.Normal);
                    });

                    var (md5Hash, sha256Hash) = f.IsDeviceSource
                        ? await _hashService.ComputeHashPairAsync(
                            () => OpenDeviceStream(f),
                            f.FileSizeBytes, Md5Enabled, Sha256Enabled, token, hashProgress)
                        : await _hashService.ComputeHashPairAsync(
                            f.FullPath, Md5Enabled, Sha256Enabled, token, hashProgress);

                    TimeSpan hashDuration = DateTime.Now - hashStart;
                    if (Md5Enabled)    _detailedLogService.LogHash("MD5",     md5Hash    ?? "N/A", hashDuration);
                    if (Sha256Enabled) _detailedLogService.LogHash("SHA-256", sha256Hash ?? "N/A", hashDuration);

                    // Dispatcher.Invoke (blocking) guarantees this runs after
                    // all queued InvokeAsync callbacks have been superseded
_fileProgressActive = false;
Application.Current.Dispatcher.Invoke(() =>
{
    f.Md5                = md5Hash    ?? "";
    f.Sha256             = sha256Hash ?? "";
    f.CopyStatus         = "Hash Complete";
    f.VerificationResult = "N/A";
    CurrentFileProgress  = 100;
});

                    _custodyService.LogAction("Hash Calculated", f.FullPath, sha256Hash ?? "");
                }
                catch (OperationCanceledException)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus         = "Cancelled";
                        f.VerificationResult = "Cancelled";
                    });
                    _detailedLogService.LogEvent("ERROR", "Operation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus         = "Failed";
                        f.VerificationResult = "Error";
                        f.ErrorDetail        = ex.Message;
                    });
                    _detailedLogService.LogEvent("ERROR", "Hash calculation failed", ex.Message);
                }

                _detailedLogService.LogFileEnd(f.FileName, f.CopyStatus, DateTime.Now - fileStartTime);
                ProgressValue = 100.0 * _current / _total;
                await Task.Delay(50);
            }

            CurrentFileText     = "Done";
            CurrentFileProgress = 100;
        }

        // ── Copy and verify mode ──────────────────────────────────────────

        private async Task CopyAndVerifyAsync(CancellationToken token)
        {
            var list = Files.Where(f => f.IsSelected).ToList();
            _total   = list.Count;
            _current = 0;

            _detailedLogService.LogEvent("PROCESS", $"Starting copy and verify for {_total} files");

            foreach (var f in list)
            {
                DateTime fileStartTime = DateTime.Now;
                _detailedLogService.LogFileStart(f.FileName, _current + 1, _total);

                token.ThrowIfCancellationRequested();
                _current++;
                OnPropertyChanged(nameof(ProgressText));
                f.ResetResults();

                string relativePath    = PathHelper.GetRelativePath(f.FullPath, _baseSourceFolder);
                string destFilePath    = Path.Combine(_destinationPath, relativePath);
                f.DestPath             = destFilePath;
                bool fileAlreadyExists = File.Exists(destFilePath);

                // ── Phase 1: Hash source ──────────────────────────────────

                Application.Current.Dispatcher.Invoke(() =>
                {
                    f.CopyStatus        = "Hashing source... 0%";
                    CurrentFileText     = $"Hashing source: {f.FileName}";
                    CurrentFileProgress = 0;

                });
                _fileProgressActive = true;

                string? srcMd5 = null, srcSha256 = null;
                DateTime srcHashStart = DateTime.Now;

                try
                {
                    var srcHashProgress = new Progress<HashProgressEventArgs>(args =>
                    {
Application.Current.Dispatcher.InvokeAsync(() =>
{
    if (!_fileProgressActive) return;
    CurrentFileProgress = args.PercentComplete;
    f.CopyStatus        = $"Hashing source... {(int)args.PercentComplete}%";
}, DispatcherPriority.Normal);
                    });

                    (srcMd5, srcSha256) = f.IsDeviceSource
                        ? await _hashService.ComputeHashPairAsync(
                            () => OpenDeviceStream(f),
                            f.FileSizeBytes, Md5Enabled, Sha256Enabled, token, srcHashProgress)
                        : await _hashService.ComputeHashPairAsync(
                            f.FullPath, Md5Enabled, Sha256Enabled, token, srcHashProgress);

                    TimeSpan srcHashDuration = DateTime.Now - srcHashStart;
                    if (Md5Enabled)    _detailedLogService.LogHash("MD5 (Source)",     srcMd5    ?? "N/A", srcHashDuration);
                    if (Sha256Enabled) _detailedLogService.LogHash("SHA-256 (Source)", srcSha256 ?? "N/A", srcHashDuration);

                    _fileProgressActive = false;
Application.Current.Dispatcher.Invoke(() =>
{
    f.Md5               = srcMd5    ?? "";
    f.Sha256            = srcSha256 ?? "";
    f.CopyStatus        = "Source hashed";
    CurrentFileProgress = 100;
});

                    _custodyService.LogAction("Source Hash Calculated", f.FullPath, srcSha256 ?? "");
                }
                catch (OperationCanceledException)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus         = "Cancelled";
                        f.VerificationResult = "Cancelled";
                    });
                    _detailedLogService.LogEvent("ERROR", "Operation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus         = "Failed";
                        f.VerificationResult = "Source: Error";
                        f.ErrorDetail        = ex.Message;
                    });
                    _detailedLogService.LogEvent("ERROR", "Source hash failed", ex.Message);
                    continue;
                }

                // ── Phase 2: Copy ─────────────────────────────────────────

                if (!fileAlreadyExists)
                {
                    try
                    {
                        string? destDirectory = Path.GetDirectoryName(destFilePath);
                        if (!string.IsNullOrEmpty(destDirectory))
                            Directory.CreateDirectory(destDirectory);
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            f.CopyStatus         = "Failed";
                            f.VerificationResult = "Cannot create destination";
                            f.ErrorDetail        = ex.Message;
                        });
                        _detailedLogService.LogEvent("ERROR", "Failed to create destination directory", ex.Message);
                        continue;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus        = "Copying... 0%";
                        CurrentFileText     = $"Copying: {relativePath}";
                        CurrentFileProgress = 0;
                    });
                    _fileProgressActive = true;

                    DateTime copyStart = DateTime.Now;
                    bool copyOk = await CopyFileWithProgressAsync(f, destFilePath, token);
                    if (!copyOk) continue;

                    _fileProgressActive = false;

                    TimeSpan copyDuration = DateTime.Now - copyStart;
                    _detailedLogService.LogCopy(f.FullPath, destFilePath, f.FileSizeBytes, copyDuration);
                    _custodyService.LogAction("File Copied", destFilePath);
                }
                else
                {
                    _detailedLogService.LogEvent("COPY", "File already exists at destination", destFilePath);
                }

                // ── Phase 3: Hash destination ─────────────────────────────

                token.ThrowIfCancellationRequested();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    f.CopyStatus        = "Verifying... 0%";
                    CurrentFileText     = $"Verifying: {f.FileName}";
                    CurrentFileProgress = 0;
                });
                _fileProgressActive = true;

                DateTime dstHashStart = DateTime.Now;

                try
                {
                    var destHashProgress = new Progress<HashProgressEventArgs>(args =>
                    {
Application.Current.Dispatcher.InvokeAsync(() =>
{
    if (!_fileProgressActive) return;
    CurrentFileProgress = args.PercentComplete;
    f.CopyStatus        = $"Verifying... {(int)args.PercentComplete}%";
}, DispatcherPriority.Normal);
                    });

                    var (dstMd5, dstSha256) = await _hashService.ComputeHashPairAsync(
                        destFilePath, Md5Enabled, Sha256Enabled, token, destHashProgress);

                    TimeSpan dstHashDuration = DateTime.Now - dstHashStart;
                    if (Md5Enabled)    _detailedLogService.LogHash("MD5 (Dest)",     dstMd5    ?? "N/A", dstHashDuration);
                    if (Sha256Enabled) _detailedLogService.LogHash("SHA-256 (Dest)", dstSha256 ?? "N/A", dstHashDuration);

                    bool md5Match    = !Md5Enabled    || srcMd5    == dstMd5;
                    bool sha256Match = !Sha256Enabled || srcSha256 == dstSha256;
                    bool verified    = md5Match && sha256Match;

                f.TransferDate      = DateTime.Now.ToString("g");
f.TransferDateValue = DateTime.Now;
                    f.VerifyDate   = f.TransferDate;

                    // Dispatcher.Invoke (blocking) ensures this always
                    // executes after any pending InvokeAsync callbacks
                   _fileProgressActive = false;
Application.Current.Dispatcher.Invoke(() =>
{
    f.DestMd5            = dstMd5    ?? "";
    f.DestSha256         = dstSha256 ?? "";
    f.CopyStatus         = verified
        ? (fileAlreadyExists ? "Verified (Existing)" : "Verified")
        : "Hash Mismatch";
    f.VerificationResult = verified ? f.CopyStatus : "MISMATCH";
    CurrentFileProgress  = 100;
});

                    _detailedLogService.LogVerification(f.VerificationResult, md5Match, sha256Match);
                    _custodyService.LogAction(
                        verified ? "Verification Success" : "VERIFICATION MISMATCH",
                        destFilePath, dstSha256 ?? "");
                }
                catch (OperationCanceledException)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus         = "Cancelled";
                        f.VerificationResult = "Cancelled";
                    });
                    _detailedLogService.LogEvent("ERROR", "Operation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus         = "Failed";
                        f.VerificationResult = "Dest hash failed";
                        f.ErrorDetail        = ex.Message;
                    });
                    _detailedLogService.LogEvent("ERROR", "Destination hash failed", ex.Message);
                    continue;
                }

                _detailedLogService.LogFileEnd(f.FileName, f.CopyStatus, DateTime.Now - fileStartTime);
                ProgressValue = 100.0 * _current / _total;
                await Task.Delay(50);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentFileText     = "Done";
                CurrentFileProgress = 100;
            });

            if (!string.IsNullOrWhiteSpace(_secondaryDestinationPath))
                await CopyAndVerifySecondaryAsync(list, token);
        }

        // ── Secondary copy and verify ─────────────────────────────────────

        private async Task CopyAndVerifySecondaryAsync(
            System.Collections.Generic.List<FileHashEntry> list,
            CancellationToken token)
        {
            _total   = list.Count;
            _current = 0;

            _detailedLogService.LogEvent("PROCESS",
                $"Starting secondary copy for {_total} files to: {_secondaryDestinationPath}");

            foreach (var f in list)
            {
                DateTime fileStartTime = DateTime.Now;
                token.ThrowIfCancellationRequested();
                _current++;
                OnPropertyChanged(nameof(ProgressText));

                string primaryStatus   = f.CopyStatus;
                string relativePath    = PathHelper.GetRelativePath(f.FullPath, _baseSourceFolder);
                string secDestFilePath = Path.Combine(_secondaryDestinationPath, relativePath);
                f.SecondaryDestPath    = secDestFilePath;
                bool fileAlreadyExists = File.Exists(secDestFilePath);

                // ── Copy to secondary ─────────────────────────────────────
                if (!fileAlreadyExists)
                {
                    try
                    {
                        string? destDir = Path.GetDirectoryName(secDestFilePath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);
                    }
                    catch (Exception ex)
                    {
                        bool primaryOk = primaryStatus is "Verified" or "Verified (Existing)";
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            f.CopyStatus  = primaryOk ? "Verified (Secondary Failed)" : "Failed (Both)";
                            f.ErrorDetail = ex.Message;
                        });
                        _detailedLogService.LogEvent("ERROR", "Failed to create secondary destination directory", ex.Message);
                        continue;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus        = "Copying (Secondary)... 0%";
                        CurrentFileText     = $"Secondary copy: {relativePath}";
                        CurrentFileProgress = 0;
                    });
                    _fileProgressActive = true;

                    bool copyOk = await CopyFileWithProgressAsync(f, secDestFilePath, token);
                    _fileProgressActive = false;

                    if (!copyOk)
                    {
                        bool primaryOk = primaryStatus is "Verified" or "Verified (Existing)";
                        Application.Current.Dispatcher.Invoke(() =>
                            f.CopyStatus = primaryOk ? "Verified (Secondary Failed)" : "Failed (Both)");
                        _detailedLogService.LogEvent("ERROR", "Secondary copy failed", f.FileName);
                        ProgressValue = 100.0 * _current / _total;
                        continue;
                    }
                }

                // ── Hash secondary destination ─────────────────────���──────
                token.ThrowIfCancellationRequested();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    f.CopyStatus        = "Verifying (Secondary)... 0%";
                    CurrentFileText     = $"Secondary verify: {f.FileName}";
                    CurrentFileProgress = 0;
                });
                _fileProgressActive = true;

                try
                {
                    var hashProgress = new Progress<HashProgressEventArgs>(args =>
                    {
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (!_fileProgressActive) return;
                            CurrentFileProgress = args.PercentComplete;
                            f.CopyStatus        = $"Verifying (Secondary)... {(int)args.PercentComplete}%";
                        }, DispatcherPriority.Normal);
                    });

                    var (secMd5, secSha256) = await _hashService.ComputeHashPairAsync(
                        secDestFilePath, Md5Enabled, Sha256Enabled, token, hashProgress);

                    bool md5Match    = !Md5Enabled    || f.Md5    == secMd5;
                    bool sha256Match = !Sha256Enabled || f.Sha256 == secSha256;
                    bool secVerified = md5Match && sha256Match;
                    bool primaryOk   = primaryStatus is "Verified" or "Verified (Existing)";

                    _fileProgressActive = false;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.SecondaryDestMd5    = secMd5    ?? "";
                        f.SecondaryDestSha256 = secSha256 ?? "";
                        f.CopyStatus = (primaryOk, secVerified, fileAlreadyExists) switch
                        {
                            (true,  true,  false) => "Verified (Primary + Secondary)",
                            (true,  true,  true)  => "Verified (Primary + Secondary Existing)",
                            (true,  false, _)     => "Verified (Secondary Mismatch)",
                            (false, true,  _)     => "Primary Failed / Secondary OK",
                            _                     => "Failed (Both)"
                        };
                        CurrentFileProgress = 100;
                    });

                    _detailedLogService.LogEvent("SECONDARY",
                        secVerified ? "Secondary verification: PASS" : "Secondary verification: FAIL",
                        f.FileName);
                }
                catch (OperationCanceledException)
                {
                    _fileProgressActive = false;
                    Application.Current.Dispatcher.Invoke(() => f.CopyStatus = "Secondary: Cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _fileProgressActive = false;
                    bool primaryOk = primaryStatus is "Verified" or "Verified (Existing)";
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        f.CopyStatus  = primaryOk ? "Verified (Secondary Failed)" : "Failed (Both)";
                        f.ErrorDetail = ex.Message;
                    });
                    _detailedLogService.LogEvent("ERROR", "Secondary hash failed", ex.Message);
                }

                ProgressValue = 100.0 * _current / _total;
                await Task.Delay(50);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentFileText     = "Done";
                CurrentFileProgress = 100;
            });
        }

        // ── Copy with progress ────────────────────────────────────────────

        private async Task<bool> CopyFileWithProgressAsync(
            FileHashEntry entry,
            string destinationPath,
            CancellationToken token)
        {
            void OnProgressChanged(object? sender, CopyProgressEventArgs e)
            {
Application.Current.Dispatcher.InvokeAsync(() =>
{
    if (!_fileProgressActive) return;
    CurrentFileProgress = e.Percentage;
    entry.CopyStatus    = e.Percentage < 100
        ? $"Copying... {(int)e.Percentage}%"
        : entry.CopyStatus;
}, DispatcherPriority.Normal);
            }

            _fileCopyService.ProgressChanged += OnProgressChanged;

            try
            {
                var result = entry.IsDeviceSource
                    ? await _fileCopyService.CopyFromStreamWithProgressAsync(
                        () => OpenDeviceStream(entry),
                        entry.FileSizeBytes, destinationPath, token)
                    : await _fileCopyService.CopyFileWithProgressAsync(
                        entry.FullPath, destinationPath, token);

                if (result.Success) return true;

                if (result.IsCancelled)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        entry.CopyStatus         = "Cancelled";
                        entry.VerificationResult = "Cancelled";
                    });
                    throw new OperationCanceledException(token);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    entry.CopyStatus         = "Failed";
                    entry.VerificationResult = "Copy Failed";
                    entry.ErrorDetail        = result.ErrorMessage;
                });
                return false;
            }
            finally
            {
                _fileCopyService.ProgressChanged -= OnProgressChanged;
            }
        }

        // ── Validation ────────────────────────────────────────────────────

        private bool CheckDiskSpace()
        {
            try
            {
                long required = Files.Where(f => f.IsSelected).Sum(f => f.FileSizeBytes);
                var (hasSpace, requiredMb, freeMb) = DriveInfoHelper.CheckDiskSpace(_destinationPath, required);

                if (!hasSpace)
                {
                    var result = MessageBox.Show(
                        $"The destination drive may not have enough free space.\n\n" +
                        $"Required : {requiredMb:N0} MB\n" +
                        $"Available: {freeMb:N0} MB\n\n" +
                        $"Do you want to continue anyway?",
                        "Low Disk Space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.No) return false;
                }

                if (!string.IsNullOrWhiteSpace(_secondaryDestinationPath))
                {
                    var (hasSecSpace, secReqMb, secFreeMb) = DriveInfoHelper.CheckDiskSpace(_secondaryDestinationPath, required);
                    if (!hasSecSpace)
                    {
                        var result = MessageBox.Show(
                            $"The secondary destination drive may not have enough free space.\n\n" +
                            $"Required : {secReqMb:N0} MB\n" +
                            $"Available: {secFreeMb:N0} MB\n\n" +
                            $"Do you want to continue anyway?",
                            "Low Disk Space (Secondary)", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.No) return false;
                    }
                }

                return true;
            }
            catch { return true; }
        }

        private bool ValidateAlgorithms()
        {
            if (Md5Enabled || Sha256Enabled) return true;
            MessageBox.Show("Please select at least one hash algorithm.",
                "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private bool ValidateFileSelection()
        {
            if (Files.Any(f => f.IsSelected)) return true;
            MessageBox.Show("No files selected. Please select at least one file.",
                "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        // ── Screenshot ────────────────────────────────────────────────────

        private void CaptureScreenshot()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null)
                {
                    MessageBox.Show("Cannot capture screenshot - window not found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var rtb = new RenderTargetBitmap(
                    (int)mainWindow.ActualWidth, (int)mainWindow.ActualHeight,
                    96, 96, PixelFormats.Pbgra32);
                rtb.Render(mainWindow);

                var dlg = new SaveFileDialog
                {
                    Filter     = "PNG Image (*.png)|*.png",
                    FileName   = $"HashDash_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    DefaultExt = ".png"
                };

                if (dlg.ShowDialog() != true) return;

                using var fs = new FileStream(dlg.FileName, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                encoder.Save(fs);

                MessageBox.Show($"Screenshot saved to:\n{dlg.FileName}", "Screenshot Saved",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                _custodyService.LogAction("Screenshot Captured", dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing screenshot: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Database logging ──────────────────────────────────────────────

        private async Task LogProcessToDatabase(string status, string? errorMessage)
        {
            try
            {
                await Task.Run(() =>
                {
                    var log = new ProcessLog
                    {
                        ExaminerName                   = _examinerName,
                        LabCaseRef                     = _labCaseRef,
                        ExhibitRef                     = _exhibitRef,
                        ComputerName                   = ComputerName,
                        SourcePath                     = _sourcePath,
                        SourceDriveLabel               = _sourceDriveLabel,
                        DestinationPath                = _destinationPath,
                        DestinationDriveLabel          = _destinationDriveLabel,
                        SecondaryDestinationPath       = _secondaryDestinationPath,
                        SecondaryDestinationDriveLabel = _secondaryDestinationDriveLabel,
                        Md5Enabled                     = _md5Enabled,
                        Sha256Enabled                  = _sha256Enabled,
                        FileCount                      = Files.Count(f => f.IsSelected),
                        TotalSizeBytes                 = Files.Where(f => f.IsSelected).Sum(f => f.FileSizeBytes),
                        StartDateTime                  = _processStartTime,
                        EndDateTime                    = DateTime.Now,
                        Status                         = status,
                        ErrorMessage                   = errorMessage,
                        AutomationMode                 = _automationEnabled,
                        PdfReportPath                  = null
                    };

                    int id = _databaseService.InsertProcessLog(log);
                    _databaseService.InsertFileResults(id, Files.Where(f => f.IsSelected).ToList());
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database logging failed: {ex.Message}");
            }
        }

        // ── Session ID ────────────────────────────────────────────────────

        /// <summary>
        /// Generates a collision-free session ID from case ref, source label, destination label
        /// and transfer start timestamp.  All non-alphanumeric characters are stripped/collapsed
        /// so the result is safe to use as a filename component and a DB unique key.
        /// </summary>
        private string GenerateSessionId(string? destDriveLabel = null)
        {
            // Keep only letters, digits, hyphen, underscore.
            // Everything else (brackets, colon, backslash, space …) → dash; runs → single dash.
            static string Sanitize(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                var sb = new System.Text.StringBuilder();
                foreach (char c in s)
                {
                    if (char.IsLetterOrDigit(c)) sb.Append(c);
                    else if (c is ' ' or '\\' or '/' or '(' or ')' or ':') sb.Append('-');
                    // all other punctuation is dropped
                }
                string result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
                return result.Length == 0 ? "X" : result;
            }

            string effectiveDest = destDriveLabel ?? _destinationDriveLabel;

            string caseRef   = string.IsNullOrWhiteSpace(_labCaseRef)    ? "NOCASE" : Sanitize(_labCaseRef);
            string srcLabel  = string.IsNullOrWhiteSpace(_sourceDriveLabel) ? "SRC"  : Sanitize(_sourceDriveLabel);
            string dstLabel  = string.IsNullOrWhiteSpace(effectiveDest)    ? "DST"  : Sanitize(effectiveDest);
            string timestamp = _processStartTime.ToString("yyyyMMdd_HHmmss");
            return $"{caseRef}_{srcLabel}_{dstLabel}_{timestamp}";
        }

        // ── PDF report ────────────────────────────────────────────────────

        private async Task GenerateAutomatedPdfReport()
        {
            if (string.IsNullOrWhiteSpace(_destinationPath)) return;

            try
            {
                await Task.Run(() =>
                {
                    string logsFolder = Path.Combine(_destinationPath, "Data Transfer Logs");
                    Directory.CreateDirectory(logsFolder);

                    string timestamp   = _processStartTime.ToString("dd-MM-yy_HH-mm");
                    string safeExhibit = _exhibitRef.Replace(" ", "").Replace("/", "-").Replace("\\", "-");
                    string safeCaseRef = _labCaseRef.Replace(" ", "").Replace("/", "-").Replace("\\", "-");
                    string basePdfPath = Path.Combine(logsFolder, $"{safeExhibit}-{safeCaseRef}_{timestamp}.pdf");
                    var selectedFiles  = Files.Where(f => f.IsSelected).ToList();
                    int pdfCount = ExportPdfInChunks(basePdfPath, selectedFiles);

                    GenerateSessionJson(logsFolder, selectedFiles, timestamp);

                    Application.Current.Dispatcher.Invoke(() =>
                        CurrentFileText = pdfCount == 1
                            ? $"PDF report saved: {Path.GetFileName(basePdfPath)}"
                            : $"PDF reports saved: {pdfCount} files");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF generation failed: {ex.Message}");
            }
        }

        // ── Session JSON ──────────────────────────────────────────────────
        //
        // One JSON file is produced per transfer session regardless of how many
        // destinations were used.  Both destination paths and their individual
        // transfer statuses are embedded in the single record so CaseTrace sees
        // one ingestion event (no duplicate-session-ID problem).
        // The file is written into every destination's "Data Transfer Logs" folder
        // AND copied to the CaseTrace watch folder for auto-ingest.

        private void GenerateSessionJson(string primaryDirectory, List<FileHashEntry> files, string timestamp)
        {
            try
            {
                var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                bool hasSecondary = !string.IsNullOrWhiteSpace(_secondaryDestinationPath);

                string safeExhibit = _exhibitRef.Replace(" ", "").Replace("/", "-").Replace("\\", "-");
                string safeCaseRef = _labCaseRef.Replace(" ", "").Replace("/", "-").Replace("\\", "-");
                string fileName    = $"{safeExhibit}-{safeCaseRef}_{timestamp}_Session.json";

                DateTime endTime  = DateTime.Now;
                TimeSpan duration = endTime - _processStartTime;
                string   pcGuid   = GetMachineGuid();

                var sessionData = new
                {
                    SessionId                       = _sessionId,
                    AppVersion                      = AppVersion,
                    TransferStatus                  = ResolvePrimaryTransferStatus(files),
                    SecondaryTransferStatus         = hasSecondary
                                                        ? ResolveSecondaryTransferStatus(files)
                                                        : (string?)null,
                    TotalDuration                   = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}",
                    PcGuid                          = pcGuid,
                    ExaminerName                    = _examinerName,
                    LabCaseRef                      = _labCaseRef,
                    ExhibitRef                      = _exhibitRef,
                    ComputerName,
                    SourcePath                      = _sourcePath,
                    SourceDriveLabel                = _sourceDriveLabel,
                    DestinationPath                 = _destinationPath,
                    DestinationDriveLabel           = _destinationDriveLabel,
                    SecondaryDestinationPath        = hasSecondary ? _secondaryDestinationPath        : (string?)null,
                    SecondaryDestinationDriveLabel  = hasSecondary ? _secondaryDestinationDriveLabel  : (string?)null,
                    Md5Enabled                      = _md5Enabled,
                    Sha256Enabled                   = _sha256Enabled,
                    StartDateTime                   = _processStartTime.ToString("O"),
                    EndDateTime                     = endTime.ToString("O"),
                    FileCount                       = files.Count,
                    TotalSizeBytes                  = files.Sum(f => f.FileSizeBytes)
                };

                string json = System.Text.Json.JsonSerializer.Serialize(sessionData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                // Write to primary destination's Data Transfer Logs folder
                File.WriteAllText(Path.Combine(primaryDirectory, fileName), json, utf8NoBom);

                // Mirror the same file to the secondary destination folder
                if (hasSecondary)
                {
                    string secondaryLogsDir = Path.Combine(_secondaryDestinationPath, "Data Transfer Logs");
                    Directory.CreateDirectory(secondaryLogsDir);
                    File.WriteAllText(Path.Combine(secondaryLogsDir, fileName), json, utf8NoBom);
                }

                // Drop a copy into the CaseTrace watch folder for auto-ingest (one copy only)
                try
                {
                    string logDir = _settingsService.Settings.LogDirectory;
                    if (string.IsNullOrWhiteSpace(logDir))
                        logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

                    Directory.CreateDirectory(logDir);
                    File.WriteAllText(Path.Combine(logDir, fileName), json, utf8NoBom);
                    System.Diagnostics.Debug.WriteLine($"[HashDash] CaseTrace copy written to: {Path.Combine(logDir, fileName)}");
                }
                catch (Exception exCopy)
                {
                    System.Diagnostics.Debug.WriteLine($"[HashDash] CaseTrace copy failed: {exCopy.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HashDash] Session JSON generation failed: {ex.Message}");
            }
        }

        /// <summary>Pass when all files are verified on the primary destination.</summary>
        private static string ResolvePrimaryTransferStatus(List<FileHashEntry> files)
        {
            if (files.Count == 0) return "Fail";

            int verified = files.Count(f =>
                f.CopyStatus is "Verified"
                             or "Verified (Existing)"
                             or "Verified (Primary + Secondary)"
                             or "Verified (Primary + Secondary Existing)"
                             or "Verified (Secondary Failed)"   // primary was fine, secondary failed
                             or "Hash Complete");

            if (verified == files.Count) return "Pass";
            if (verified == 0)           return "Fail";
            return "Partial Transfer";
        }

        /// <summary>Pass when all files are verified on the secondary destination.</summary>
        private static string ResolveSecondaryTransferStatus(List<FileHashEntry> files)
        {
            if (files.Count == 0) return "Fail";

            int verified = files.Count(f =>
                f.CopyStatus is "Verified (Primary + Secondary)"
                             or "Verified (Primary + Secondary Existing)");

            if (verified == files.Count) return "Pass";
            if (verified == 0)           return "Fail";
            return "Partial Transfer";
        }

        private static string GetMachineGuid()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography", writable: false);
                return key?.GetValue("MachineGuid") as string ?? Guid.Empty.ToString();
            }
            catch
            {
                return Guid.Empty.ToString();
            }
        }

        // ── Automation ────────────────────────────────────────────────────

        public void ToggleAutomation()
        {
            if (_automationEnabled)
            {
                if (_isProcessing)
                {
                    MessageBox.Show("Cannot disable automation while processing is active.",
                        "Automation Active", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StopMonitoring();
                AutomationEnabled = false;
                _toastService.ShowSuccess("Automation Disabled", "Normal mode restored");
            }
            else
            {
                if (!ValidateFieldsForAutomation()) return;

                if (string.IsNullOrWhiteSpace(_destinationPath))
                {
                    MessageBox.Show(
                        "Destination folder is required for automation mode.\n\n" +
                        "Please select a destination folder before enabling automation.",
                        "Destination Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(_destinationPath))
                {
                    MessageBox.Show(
                        $"Destination folder does not exist:\n{_destinationPath}\n\n" +
                        "Please select a valid destination folder.",
                        "Invalid Destination", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AutomationConfig config;

                if (string.IsNullOrWhiteSpace(_triggerFilePattern))
                {
                    var settingsWindow = new SettingsWindow(_settingsService) { Owner = Application.Current.MainWindow };
                    if (settingsWindow.ShowDialog() == true)
                    {
                        config = settingsWindow.GetAutomationConfig();
                        TriggerFilePattern = config.TriggerFilePattern;
                        _settingsService.Settings.TriggerFilePattern = config.TriggerFilePattern;
                        _settingsService.SaveSettings();
                    }
                    else return;
                }
                else
                {
                    config = new AutomationConfig
                    {
                        TriggerFilePattern    = _triggerFilePattern,
                        IncludeSubdirectories = true
                    };
                }

                config.WatchFolder = _sourcePath;
                _monitorService.UpdateConfig(config);
                AutomationEnabled = true;
                StartMonitoring();

                _toastService.ShowSuccess("Automation Enabled",
                    $"Monitoring: {Path.GetFileName(_sourcePath)}\nTrigger: {config.TriggerFilePattern}");
            }
        }

        private bool ValidateFieldsForAutomation()
        {
            if (string.IsNullOrWhiteSpace(_examinerName))
            {
                MessageBox.Show("Please enter Examiner Name before enabling automation.",
                    "Required Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_labCaseRef))
            {
                MessageBox.Show("Please enter Lab Case Reference before enabling automation.",
                    "Required Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_exhibitRef))
            {
                MessageBox.Show("Please enter Exhibit Reference before enabling automation.",
                    "Required Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_sourcePath))
            {
                MessageBox.Show("Please select Source folder before enabling automation.",
                    "Required Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!Directory.Exists(_sourcePath))
            {
                MessageBox.Show(
                    $"Source folder does not exist:\n{_sourcePath}\n\nPlease select a valid source folder.",
                    "Invalid Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!_md5Enabled && !_sha256Enabled)
            {
                MessageBox.Show("Please select at least one hash algorithm.",
                    "Required Setting", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void OnFolderDetected(object? sender, string folderPath)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(_sourcePath) || !Directory.Exists(_sourcePath))
                    {
                        MessageBox.Show(
                            $"Source folder is not valid or no longer exists:\n{_sourcePath}\n\n" +
                            "Please reconfigure automation.",
                            "Invalid Source", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    _detailedLogService.LogEvent("AUTOMATION", "Trigger detected",
                        $"Trigger file found in: {folderPath}");
                    _detailedLogService.LogEvent("AUTOMATION", "Processing source", _sourcePath);

                    Files.Clear();
                    _filePathSet.Clear();
                    _integrityService.Clear();
                    _baseSourceFolder = _sourcePath;

                    await LoadFilesAsync(_sourcePath);

                    _detailedLogService.LogEvent("AUTOMATION", $"Loaded {Files.Count} files from source");

                    var settings = _settingsService.Settings;
                    if (settings.AutomationDelayEnabled && settings.AutomationDelayMinutes > 0)
                    {
                        int delayMs = settings.AutomationDelayMinutes * 60_000;
                        AutomationStatusMessage =
                            $"AUTOMATION ACTIVE - Starting in {settings.AutomationDelayMinutes} minute(s)...";
                        _detailedLogService.LogEvent("AUTOMATION",
                            $"Startup delay: {settings.AutomationDelayMinutes} minute(s)");
                        await Task.Delay(delayMs);
                    }
                    else
                    {
                        await Task.Delay(500);
                    }

                    ExecuteRun();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error processing automation trigger: {ex.Message}",
                        "Automation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void StartMonitoring()
        {
            try
            {
                _monitorService.Start();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(
                    $"Invalid automation configuration:\n{ex.Message}\n\n" +
                    "Please check your watch folder settings.",
                    "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                AutomationEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting file monitoring:\n{ex.Message}",
                    "Monitoring Error", MessageBoxButton.OK, MessageBoxImage.Error);
                AutomationEnabled = false;
            }
        }

        private void StopMonitoring() => _monitorService.Stop();

        // ── Utility commands ──────────────────────────────────────────────

        private void Cancel() => _cts?.Cancel();

        private void Clear()
        {
            if (Files.Count == 0) return;

            if (MessageBox.Show("Clear all files from the list?", "Confirm Clear",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            Files.Clear();
            _filePathSet.Clear();
            SourcePath               = "";
            DestinationPath          = "";
            SecondaryDestinationPath = "";
            _baseSourceFolder        = "";
            _sourceIsMtp             = false;
            _sourceIsIos             = false;
            _sourceMtpDeviceId       = null;
            _sourceMtpFriendlyName   = "";
            _mtpDeviceService.Disconnect();
            _iosService.Disconnect();
            _integrityService.Clear();
            _custodyService.LogAction("File List Cleared", "");
            ResetProgress();
            CompletionMessage = "";
        }

        private void SetOperationState(bool running)
        {
            _isProcessing   = running;
            IsProcessing    = running;
            IsUiEnabled     = !running;
            IsCancelEnabled = running;
            IsCloseEnabled  = !running || !_automationEnabled;

            if (!running) ResetProgress();
        }

        private void ResetProgress()
        {
            _current            = 0;
            _total              = 0;
            ProgressValue       = 0;
            CurrentFileProgress = 0;
            CurrentFileText     = "";
            OnPropertyChanged(nameof(ProgressText));
        }

        private void ExportCsv()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter     = "CSV (*.csv)|*.csv",
                    FileName   = _fileNamingService.GenerateExportFileName("csv", ExhibitRef),
                    DefaultExt = ".csv"
                };
                if (dlg.ShowDialog() != true) return;

                int fileCount = _exportService.SaveCsvChunked(
                    dlg.FileName, Files, ComputerName, ExaminerName,
                    LabCaseRef, ExhibitRef, Md5Enabled, Sha256Enabled, CsvRowsPerFile);

                _toastService.ShowSuccess("CSV Export Complete",
                    fileCount == 1
                        ? $"File saved to: {Path.GetFileName(dlg.FileName)}"
                        : $"{fileCount} files saved — dataset split into {fileCount} CSVs of {CsvRowsPerFile:N0} rows each.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting CSV: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf()
        {
            try
            {
                if (Files.Count == 0)
                {
                    MessageBox.Show("No data to export.", "No Data",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter     = "PDF Document (*.pdf)|*.pdf",
                    FileName   = _fileNamingService.GenerateExportFileName("pdf", ExhibitRef),
                    DefaultExt = ".pdf"
                };
                if (dlg.ShowDialog() != true) return;

                int pdfCount = ExportPdfInChunks(dlg.FileName, Files.ToList());

                _toastService.ShowSuccess("PDF Export Complete",
                    pdfCount == 1
                        ? $"File saved to: {Path.GetFileName(dlg.FileName)}"
                        : $"{pdfCount} PDFs saved — dataset split into files of up to {PdfRowsPerFile:N0} rows each.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting PDF: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int ExportPdfInChunks(string basePdfPath, List<FileHashEntry> filesToExport)
        {
            if (filesToExport.Count == 0) return 0;

            int chunkCount = (int)Math.Ceiling(filesToExport.Count / (double)PdfRowsPerFile);
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunkFiles = filesToExport
                    .Skip(chunkIndex * PdfRowsPerFile)
                    .Take(PdfRowsPerFile)
                    .ToList();

                string outputPath = BuildPdfChunkPath(basePdfPath, chunkIndex, chunkCount);
                _pdfReportService.GenerateManualExportPdf(
                    outputPath, _examinerName, _labCaseRef, _exhibitRef, ComputerName,
                    _sourcePath, _sourceDriveLabel, _destinationPath, _destinationDriveLabel,
                    _processStartTime, DateTime.Now, chunkFiles, _md5Enabled, _sha256Enabled,
                    _secondaryDestinationPath, _secondaryDestinationDriveLabel, _sessionId);
            }

            return chunkCount;
        }

        private static string BuildPdfChunkPath(string basePdfPath, int chunkIndex, int chunkCount)
        {
            if (chunkCount <= 1) return basePdfPath;

            string directory = Path.GetDirectoryName(basePdfPath) ?? "";
            string name      = Path.GetFileNameWithoutExtension(basePdfPath);
            string extension = Path.GetExtension(basePdfPath);
            return Path.Combine(directory, $"{name}_part{chunkIndex + 1:D2}{extension}");
        }

        private void ViewLogs()
        {
            try
            {
                string logDir = _settingsService.GetLogDirectory();
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening logs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DetectDuplicates()
        {
            var duplicates = _duplicateService.FindDuplicates(Files);
            if (duplicates.Count == 0)
            {
                MessageBox.Show("No duplicate files found.", "Duplicate Detection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new DuplicateDetectionWindow(duplicates) { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        private void ShowNotifications()
        {
            _notificationService.MarkAllAsRead();
            UnreadNotificationCount = 0;
            new NotificationWindow(_notificationService) { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        private void ClearNotifications()
        {
            _notificationService.ClearAll();
            UnreadNotificationCount = 0;
        }

        private void OpenSettings()
        {
            // Pass the shared service instance so the dialog writes directly to it —
            // no Reload() needed afterwards.
            new SettingsWindow(_settingsService) { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        public void OpenLogsWindow()
        {
            new LogsWindow(_databaseService) { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        private void Close()
        {
            if (_isProcessing && _automationEnabled)
            {
                MessageBox.Show(
                    "Cannot close while automation is processing.\n\nPlease wait for completion or disable automation.",
                    "Processing Active", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts?.Cancel();
            _clockTimer.Stop();
            StopMonitoring();
            _settingsService.Settings.Md5Enabled   = Md5Enabled;
            _settingsService.Settings.Sha256Enabled = Sha256Enabled;
            _settingsService.SaveSettings();
            Application.Current.Shutdown();
        }

        private void ExportDetailedLog()
        {
            try
            {
                string logContent = _detailedLogService.GetLogContent();
                if (string.IsNullOrWhiteSpace(logContent))
                {
                    MessageBox.Show("No detailed log available. Run a process first.", "No Log",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter     = "Text File (*.txt)|*.txt",
                    FileName   = $"{_exhibitRef}-{_labCaseRef}_{DateTime.Now:yyyyMMdd_HHmmss}_DetailedLog.txt",
                    DefaultExt = ".txt"
                };

                if (dlg.ShowDialog() != true) return;

                File.WriteAllText(dlg.FileName, logContent, Encoding.UTF8);
                _toastService.ShowSuccess("Detailed Log Exported",
                    $"File saved to: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting detailed log: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool FilterFiles(object obj)
        {
            if (obj is not FileHashEntry f) return false;

            // Status filter
            if (_statusFilter != "All")
            {
                bool statusMatch = _statusFilter switch
                {
                    "Verified"          => f.CopyStatus?.Contains("Verified",     StringComparison.OrdinalIgnoreCase) == true,
                    "Failed / Mismatch" => f.CopyStatus?.Contains("Failed",       StringComparison.OrdinalIgnoreCase) == true
                                       || f.CopyStatus?.Contains("Mismatch",     StringComparison.OrdinalIgnoreCase) == true,
                    "Hash Complete"     => f.CopyStatus?.Contains("Hash Complete",StringComparison.OrdinalIgnoreCase) == true,
                    "Cancelled"         => f.CopyStatus?.Contains("Cancelled",    StringComparison.OrdinalIgnoreCase) == true,
                    "Pending"           => string.IsNullOrEmpty(f.CopyStatus) || f.CopyStatus == "Pending",
                    _                   => true
                };
                if (!statusMatch) return false;
            }

            // Search text filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var term = _searchText.Trim();
                if (!Contains(f.FileName,    term) && !Contains(f.CopyStatus,   term)
                 && !Contains(f.Sha256,      term) && !Contains(f.DestSha256,   term)
                 && !Contains(f.Md5,         term) && !Contains(f.DestMd5,      term)
                 && !Contains(f.FileDate,    term) && !Contains(f.TransferDate, term)
                 && !Contains(f.SizeDisplay, term)
                 && !Contains(f.PhotoAlbumsDisplay, term) && !Contains(f.LibraryRole, term))
                    return false;
            }

            return true;

            static bool Contains(string? source, string t) =>
                source?.Contains(t, StringComparison.OrdinalIgnoreCase) == true;
        }

        // ── IDataErrorInfo ────────────────────────────────────────────────

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                if (!_hasAttemptedRun) return string.Empty;
                return columnName switch
                {
                    nameof(ExaminerName) when string.IsNullOrWhiteSpace(_examinerName) => "Required",
                    nameof(LabCaseRef)   when string.IsNullOrWhiteSpace(_labCaseRef)   => "Required",
                    nameof(ExhibitRef)   when string.IsNullOrWhiteSpace(_exhibitRef)   => "Required",
                    _ => string.Empty
                };
            }
        }
    }
}