using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HashDash.Models;
using HashDash.Services;

namespace HashDash.ViewModels
{
    /// <summary>iOS (Apple File Conduit) source support — see IosAfcService.</summary>
    public partial class MainViewModel
    {
        /// <summary>Folders holding Photos-library files: originals, edits, iCloud-synced originals, shared albums.</summary>
        private static readonly string[] PhotoLibraryRoots =
        {
            "DCIM", "PhotoData/Mutations", "PhotoData/CPLAssets", "PhotoData/PhotoCloudSharingData"
        };

        /// <summary>The library database (and its uncommitted WAL pages) is acquired as evidence of album structure.</summary>
        private static readonly string[] PhotoLibraryDatabaseFiles =
        {
            "PhotoData/Photos.sqlite", "PhotoData/Photos.sqlite-wal", "PhotoData/Photos.sqlite-shm"
        };

        private IosPhotoLibraryService? _photoLibrary;

        private Stream OpenDeviceStream(FileHashEntry f) =>
            f.IsIosSource
                ? _iosService.OpenRead(f.IosDevicePath!)
                : _mtpDeviceService.OpenRead(f.MtpDeviceId!, f.MtpDevicePath!);

        private async Task ConnectIosDeviceAsync(IosDeviceInfo device, bool entirePartition)
        {
            Files.Clear();
            _filePathSet.Clear();
            _photoLibrary = null;
            _mtpDeviceService.Disconnect();

            using var cts = new CancellationTokenSource();
            _cts = cts;
            SetOperationState(true);
            CurrentFileText = $"Connecting to {device.FriendlyName}...";

            try
            {
                await ConnectIosWhenUnlockedAsync(device, cts.Token);

                _custodyService.LogAction(
                    "iOS Device Connected — Read-Only AFC Session",
                    device.FriendlyName, "",
                    $"UDID: {device.Udid}; Model: {device.ProductType}; iOS {device.ProductVersion}; " +
                    "existing pairing record reused (HashDash creates no pairing); " +
                    "note: the device itself logs lockdown sessions");

                var (skipped, missing) = await LoadFilesFromIosDeviceAsync(device, entirePartition, cts.Token);

                _baseSourceFolder      = $"[{device.FriendlyName}]";
                _sourceIsMtp           = false;
                _sourceIsIos           = true;
                _sourceMtpDeviceId     = device.Udid;
                _sourceMtpFriendlyName = device.FriendlyName;
                SourcePath             = $"iOS: {device.FriendlyName}";

                _toastService.ShowSuccess("Device Connected",
                    $"Loaded {Files.Count:N0} files from {device.FriendlyName}" +
                    (_photoLibrary != null ? $" — {_photoLibrary.Assets.Count:N0} Photos library items mapped to albums" : ""));

                if (missing > 0)
                    _toastService.ShowWarning("Items Not On Device",
                        $"{missing:N0} Photos library items have no original on the device (iCloud-only). " +
                        "They cannot be acquired over USB; see the chain of custody log.");

                if (skipped.Count > 0)
                    _toastService.ShowWarning("Folders Refused",
                        $"The device refused to list {skipped.Count} folder(s): {string.Join(", ", skipped.Take(5))}" +
                        (skipped.Count > 5 ? ", ..." : ""));
            }
            catch (OperationCanceledException)
            {
                _iosService.Disconnect();
                _toastService.ShowWarning("Cancelled", $"Device scan cancelled: {device.FriendlyName}");
            }
            catch (Exception ex)
            {
                _iosService.Disconnect();
                MessageBox.Show($"Error reading from device: {ex.Message}", "Device Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cts = null;
                SetOperationState(false);
            }
        }

        /// <summary>
        /// A PC cannot unlock an iPhone — the passcode can only be entered on the device. While
        /// it is locked, keep asking the examiner to unlock it and retry until they do or cancel.
        /// Once the session is open, reads continue even if the screen locks again.
        /// </summary>
        private async Task ConnectIosWhenUnlockedAsync(IosDeviceInfo device, CancellationToken token)
        {
            bool loggedLocked = false;
            while (true)
            {
                try
                {
                    await Task.Run(() => _iosService.Connect(device), token);
                    return;
                }
                catch (DeviceLockedException)
                {
                    if (!loggedLocked)
                    {
                        _custodyService.LogAction("iOS Device Locked — Waiting For Examiner To Unlock", device.FriendlyName);
                        loggedLocked = true;
                    }
                    CurrentFileText = $"{device.FriendlyName} is locked. Unlock it with its passcode to continue — retrying automatically...";
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
            }
        }

        private async Task<(List<string> skipped, int missing)> LoadFilesFromIosDeviceAsync(
            IosDeviceInfo device, bool entirePartition, CancellationToken token)
        {
            string displayRoot = $"[{device.FriendlyName}]";
            CurrentFileText = $"Scanning {device.FriendlyName} for files...";

            var progress = new Progress<int>(count =>
                Application.Current.Dispatcher.InvokeAsync(
                    () => CurrentFileText = $"Scanning {device.FriendlyName}... {count:N0} files found",
                    DispatcherPriority.Normal));

            var roots = entirePartition ? new[] { "" } : PhotoLibraryRoots;
            var scan  = await Task.Run(() => _iosService.EnumerateFiles(roots, token, progress), token);

            if (!entirePartition)
            {
                // Acquire the library database too — it is the evidence for album membership.
                foreach (string dbFile in PhotoLibraryDatabaseFiles)
                {
                    try
                    {
                        var (length, modified) = _iosService.GetCurrentMetadata(dbFile);
                        scan.Files.Add(new AfcFileEntry { RelativePath = dbFile, Length = length, LastWriteTime = modified });
                    }
                    catch (FileNotFoundException) { /* -wal / -shm only exist while Photos has pending writes */ }
                }
            }

            CurrentFileText = "Reading the Photos library database...";
            _photoLibrary   = await Task.Run(() => LoadPhotoLibrary(device), token);

            int missing = 0;
            if (_photoLibrary != null)
            {
                _photoLibrary.ResolveMissing(scan.Files.Select(f => f.RelativePath));
                missing = _photoLibrary.MissingOnDevice.Count;
                if (missing > 0)
                    _custodyService.LogAction(
                        "Photos Library Items Not Present On Device", device.FriendlyName, "",
                        $"{missing} item(s) listed in Photos.sqlite have no original on the device (iCloud-only); " +
                        "first 50: " + string.Join("; ", _photoLibrary.MissingOnDevice.Take(50).Select(a => a.RelativePath)));
            }

            foreach (string folder in scan.SkippedFolders)
                _custodyService.LogAction("iOS Folder Refused By Device", folder);

            CurrentFileText = $"Loading {scan.Files.Count:N0} files into the list...";

            var batch = new List<FileHashEntry>(500);
            foreach (var entry in scan.Files)
            {
                string displayPath = Path.Combine(displayRoot, entry.RelativePath.Replace('/', '\\'));
                if (!_filePathSet.Add(displayPath)) continue;

                var match = _photoLibrary?.Match(entry.RelativePath);
                bool groupable = match != null && match.Role != "Thumbnail";

                var hashEntry = new FileHashEntry
                {
                    FileName      = Path.GetFileName(entry.RelativePath),
                    FullPath      = displayPath,
                    FileDate      = entry.LastWriteTime.ToString("g"),
                    FileDateValue = entry.LastWriteTime,
                    FileSizeBytes = entry.Length,
                    IsSelected    = true,
                    IsIosSource   = true,
                    IosDevicePath = entry.RelativePath,
                    PhotoGroups   = groupable ? match!.Asset.Groups.ToArray() : Array.Empty<string>(),
                    SavedByApp    = match?.Asset.ImportedBy ?? "",
                    LibraryRole   = match?.Role
                                    ?? (entry.RelativePath.StartsWith("PhotoData/Photos.sqlite", StringComparison.OrdinalIgnoreCase)
                                        ? "Library database" : "")
                };

                hashEntry.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(FileHashEntry.IsSelected))
                        UpdateSelectAllState();
                };

                batch.Add(hashEntry);
                if (batch.Count >= 500)
                {
                    foreach (var e in batch) Files.Add(e);
                    batch.Clear();
                }
            }

            foreach (var e in batch) Files.Add(e);
            return (scan.SkippedFolders, missing);
        }

        /// <summary>
        /// Copies Photos.sqlite (+ WAL/SHM) to a temporary working folder, records each copy's
        /// SHA-256 in the chain of custody, and parses album membership from the copy. The
        /// device's own database is only ever read.
        /// </summary>
        private IosPhotoLibraryService? LoadPhotoLibrary(IosDeviceInfo device)
        {
            string work = Path.Combine(Path.GetTempPath(), "HashDash", "PhotosLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            try
            {
                foreach (string dbFile in PhotoLibraryDatabaseFiles)
                {
                    if (!_iosService.Exists(dbFile)) continue;

                    string local = Path.Combine(work, Path.GetFileName(dbFile));
                    _iosService.CopyToLocal(dbFile, local);

                    using var stream = File.OpenRead(local);
                    string sha256 = Convert.ToHexString(SHA256.HashData(stream));
                    _custodyService.LogAction(
                        "Photos Library Database Read (Working Copy For Album Mapping)",
                        $"[{device.FriendlyName}]\\{dbFile.Replace('/', '\\')}", sha256,
                        "Read-only copy parsed in a temporary folder and deleted after parsing");
                }

                string sqlite = Path.Combine(work, "Photos.sqlite");
                if (!File.Exists(sqlite)) return null;

                try
                {
                    return IosPhotoLibraryService.Load(sqlite);
                }
                catch (Exception ex)
                {
                    _custodyService.LogAction("Photos Library Database Could Not Be Parsed", sqlite, "", ex.Message);
                    return null;
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(work, recursive: true); } catch { /* temp folder; best effort */ }
            }
        }
    }
}
