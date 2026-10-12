using HashDash.Services.Ios;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace HashDash.Services
{
    public class IosDeviceInfo
    {
        public string Udid { get; init; } = "";
        public long   UsbmuxId { get; init; }
        public string FriendlyName { get; init; } = "";
        public string ProductType { get; init; } = "";
        public string ProductVersion { get; init; } = "";
        /// <summary>
        /// True when Windows already holds a pairing record for this device, i.e. it already
        /// trusts this PC. Connecting reuses that pairing; no new host record is created.
        /// </summary>
        public bool IsPaired { get; init; }
    }

    public class AfcFileEntry
    {
        /// <summary>Path relative to the media root, '/'-separated, e.g. DCIM/100APPLE/IMG_0001.HEIC</summary>
        public string RelativePath { get; init; } = "";
        public long Length { get; init; }
        public DateTime LastWriteTime { get; init; }
    }

    public class AfcScanResult
    {
        public List<AfcFileEntry> Files { get; } = new();
        /// <summary>Folders the device refused to list — reported to the examiner, never silently dropped.</summary>
        public List<string> SkippedFolders { get; } = new();
    }

    /// <summary>The device is passcode-locked; the caller should ask the examiner to unlock it and retry.</summary>
    public class DeviceLockedException : InvalidOperationException
    {
        public DeviceLockedException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads an iPhone/iPad's media partition (/var/mobile/Media) over Apple File Conduit,
    /// the channel iTunes and iMazing use. Windows Portable Devices (MtpDeviceService) only
    /// gets the DCIM camera roll from Apple; AFC also exposes PhotoData: the Photos library
    /// database (album membership), iCloud-synced originals (CPLAssets), shared albums and
    /// edited renders.
    ///
    /// Implemented in-house on top of Apple Mobile Device Service (usbmux) — no third-party
    /// libraries. Forensic constraints:
    /// - Only an existing pairing record is ever used. There is no pair request anywhere in
    ///   HashDash, so connecting can never trigger "Trust This Computer?" or create a new host
    ///   record on the device.
    /// - AfcClient has no write/remove/rename/mkdir operation; files open read-only.
    /// - The phone logs lockdown sessions itself. That cannot be prevented from the host and
    ///   is recorded in the chain of custody instead.
    ///
    /// The AFC connection is a single request/response stream, so every call is serialised
    /// on _sync.
    /// </summary>
    public class IosAfcService : IDisposable
    {
        private readonly object _sync = new();
        private LockdownClient? _lockdown;
        private AfcClient?      _afc;
        private string?         _udid;

        /// <summary>
        /// Lists USB-connected iOS devices. Reads only the pairing store and values the device
        /// publishes without a session — no session is started and nothing is paired.
        /// </summary>
        public static List<IosDeviceInfo> GetConnectedDevices()
        {
            var result = new List<IosDeviceInfo>();

            foreach (var device in UsbmuxClient.ListUsbDevices().GroupBy(d => d.Udid).Select(g => g.First()))
            {
                string name = "", type = "", version = "";
                try
                {
                    using var lockdown = new LockdownClient(device.DeviceId);
                    name    = lockdown.GetValue("DeviceName")     ?? "";
                    type    = lockdown.GetValue("ProductType")    ?? "";
                    version = lockdown.GetValue("ProductVersion") ?? "";
                }
                catch
                {
                    // Identity values are cosmetic; still list the device
                }

                result.Add(new IosDeviceInfo
                {
                    Udid           = device.Udid,
                    UsbmuxId       = device.DeviceId,
                    FriendlyName   = string.IsNullOrWhiteSpace(name) ? "Apple device" : name,
                    ProductType    = type,
                    ProductVersion = version,
                    IsPaired       = PairRecord.Load(device.Udid) != null
                });
            }

            return result;
        }

        /// <summary>
        /// Opens a read-only AFC session to the media partition using the existing pairing.
        /// Throws DeviceLockedException while the device is passcode-locked.
        /// </summary>
        public void Connect(IosDeviceInfo device)
        {
            lock (_sync)
            {
                if (_afc != null && _udid == device.Udid) return;
                DisconnectCore();

                var pair = PairRecord.Load(device.Udid)
                    ?? throw new InvalidOperationException(
                        "This device has not trusted this PC. HashDash never creates a pairing itself, because the device " +
                        "would store a new host record. If you decide trust is acceptable, establish it outside HashDash " +
                        "(e.g. via Apple Devices), record that in your notes, then connect again.");

                var lockdown = new LockdownClient(device.UsbmuxId);
                try
                {
                    lockdown.StartSession(pair);
                    var (port, useTls) = lockdown.StartService("com.apple.afc");

                    var mux    = new UsbmuxClient();
                    Stream raw = mux.Connect(device.UsbmuxId, port);
                    Stream afcStream = useTls ? LockdownClient.Secure(raw, pair) : raw;

                    _afc      = new AfcClient(afcStream, mux);
                    _lockdown = lockdown;
                    _udid     = device.Udid;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
                {
                    lockdown.Dispose();
                    throw new DeviceLockedException(ex.Message);
                }
                catch
                {
                    lockdown.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// Walks the given media-partition roots folder by folder. A folder the device refuses
        /// to list goes into SkippedFolders and the walk carries on, so one restricted folder
        /// never truncates the rest of the scan.
        /// </summary>
        public AfcScanResult EnumerateFiles(
            IEnumerable<string> roots,
            CancellationToken cancellationToken = default,
            IProgress<int>? progress = null)
        {
            var result  = new AfcScanResult();
            var pending = new Stack<string>(roots.Select(Normalise).Reverse());

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string folder = pending.Pop();

                List<string> names;
                try
                {
                    lock (_sync) names = RequireAfc().ReadDirectory("/" + folder);
                }
                catch (AfcException ex) when (ex.Status == 8 && folder.Contains('/'))
                {
                    continue; // vanished mid-scan
                }
                catch (AfcException)
                {
                    result.SkippedFolders.Add(folder.Length == 0 ? "/" : folder);
                    continue;
                }

                var subfolders = new List<string>();
                foreach (string name in names)
                {
                    if (name is "." or "..") continue;
                    cancellationToken.ThrowIfCancellationRequested();

                    string relative = folder.Length == 0 ? name : folder + "/" + name;
                    var info = TryGetInfo(relative);
                    if (info == null) continue;

                    if (info.Value.isDirectory)
                    {
                        subfolders.Add(relative);
                    }
                    else if (info.Value.isFile)
                    {
                        result.Files.Add(new AfcFileEntry
                        {
                            RelativePath  = relative,
                            Length        = info.Value.length,
                            LastWriteTime = info.Value.modified
                        });

                        if (result.Files.Count % 20 == 0)
                            progress?.Report(result.Files.Count);
                    }
                }

                for (int i = subfolders.Count - 1; i >= 0; i--)
                    pending.Push(subfolders[i]);
            }

            progress?.Report(result.Files.Count);
            return result;
        }

        public bool Exists(string relativePath) => TryGetInfo(Normalise(relativePath)) != null;

        public (long length, DateTime lastWriteTime) GetCurrentMetadata(string relativePath)
        {
            var info = TryGetInfo(Normalise(relativePath))
                ?? throw new FileNotFoundException("File is no longer present on the device.", relativePath);
            return (info.length, info.modified);
        }

        /// <summary>Opens a read-only sequential stream directly off the device.</summary>
        public Stream OpenRead(string relativePath)
        {
            lock (_sync)
            {
                ulong handle = RequireAfc().OpenReadOnly("/" + Normalise(relativePath));
                return new AfcReadStream(this, handle);
            }
        }

        /// <summary>Copies a device file to a local working file (used for Photos.sqlite and its WAL).</summary>
        public void CopyToLocal(string relativePath, string localPath)
        {
            using var source = OpenRead(relativePath);
            using var target = File.Create(localPath);
            source.CopyTo(target, AfcClient.MaxReadSize);
        }

        private (bool isDirectory, bool isFile, long length, DateTime modified)? TryGetInfo(string relative)
        {
            Dictionary<string, string>? values;
            lock (_sync) values = RequireAfc().GetFileInfo("/" + relative);
            if (values == null) return null;

            values.TryGetValue("st_ifmt", out string? kind);
            long.TryParse(values.GetValueOrDefault("st_size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long size);

            // st_mtime is nanoseconds since the Unix epoch
            DateTime modified = DateTime.Now;
            if (long.TryParse(values.GetValueOrDefault("st_mtime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ns))
                modified = DateTimeOffset.FromUnixTimeMilliseconds(ns / 1_000_000).LocalDateTime;

            return (kind == "S_IFDIR", kind == "S_IFREG", size, modified);
        }

        private static string Normalise(string path) => path.Replace('\\', '/').Trim('/');

        private AfcClient RequireAfc() =>
            _afc ?? throw new InvalidOperationException("No iOS device session is open.");

        public void Disconnect()
        {
            lock (_sync) DisconnectCore();
        }

        private void DisconnectCore()
        {
            try { _afc?.Dispose(); }      catch { /* best effort */ }
            try { _lockdown?.Dispose(); } catch { /* best effort */ }
            _afc      = null;
            _lockdown = null;
            _udid     = null;
        }

        public void Dispose() => Disconnect();

        private sealed class AfcReadStream : Stream
        {
            private readonly IosAfcService _owner;
            private readonly ulong _handle;
            private bool _closed;

            public AfcReadStream(IosAfcService owner, ulong handle)
            {
                _owner  = owner;
                _handle = handle;
            }

            public override bool CanRead  => true;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0) return 0;
                lock (_owner._sync)
                    return _owner.RequireAfc().Read(_handle, buffer, offset, count);
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_closed)
                {
                    _closed = true;
                    lock (_owner._sync)
                    {
                        try { _owner._afc?.Close(_handle); } catch { /* session may already be gone */ }
                    }
                }
                base.Dispose(disposing);
            }
        }
    }
}
