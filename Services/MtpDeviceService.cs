using MediaDevices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace HashDash.Services
{
    public class MtpDeviceInfo
    {
        public string DeviceId { get; init; } = "";
        public string FriendlyName { get; init; } = "";
    }

    public class MtpFileEntry
    {
        /// <summary>Path relative to the device root, used to preserve directory structure at the destination.</summary>
        public string RelativePath { get; init; } = "";
        /// <summary>Full device path, passed back into GetFileInfo/OpenRead.</summary>
        public string DevicePath { get; init; } = "";
        public long Length { get; init; }
        public DateTime LastWriteTime { get; init; }
    }

    /// <summary>
    /// Wraps the MediaDevices library so MTP-connected devices (phones, cameras, drone
    /// controllers) can be read as a hash/copy source even though they have no real
    /// filesystem path. Every connection requests read-only access (GenericRead/Share.Read)
    /// and this service never calls any write/upload/delete/rename method on a device —
    /// hardware write-blockers don't apply to MTP, so this is the closest available
    /// procedural equivalent.
    ///
    /// MediaDevice.GetDevices() returns the SAME cached instance per device ID for as long
    /// as the device stays plugged in (it's a static cache inside the library, not a
    /// fresh object per call) — so instances must never be disposed just for a read-only
    /// property peek, and IsConnected only reflects what *we* last did, not whether the
    /// driver silently tore the session down on its own (which iOS in particular does
    /// aggressively on re-lock). Every real device operation below retries once with a
    /// forced clean reconnect if the cached session turns out to be stale.
    ///
    /// The underlying WPD COM objects are apartment-sensitive. Calling them from whatever
    /// arbitrary thread-pool thread a Task.Run happens to land on can silently get the call
    /// marshaled back onto (and block) the thread that first created the COM object — which,
    /// left to chance, can end up being the WPF UI thread, freezing the whole window with no
    /// visible progress even though Task Manager shows activity. Every call — including reads
    /// on the stream returned by OpenRead — is funneled through one dedicated, persistent STA
    /// thread this service owns, so the COM apartment is always the same thread and never the
    /// UI thread.
    /// </summary>
    public class MtpDeviceService : IDisposable
    {
        private static class StaExecutor
        {
            private static readonly BlockingCollection<Action> Queue = new();

            static StaExecutor()
            {
                var thread = new Thread(() =>
                {
                    foreach (var action in Queue.GetConsumingEnumerable())
                        action();
                })
                {
                    IsBackground = true,
                    Name = "HashDash-MtpDevice-STA"
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }

            public static T Run<T>(Func<T> func)
            {
                T result = default!;
                Exception? error = null;
                using var done = new ManualResetEventSlim(false);

                Queue.Add(() =>
                {
                    try
                    {
                        result = func();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                done.Wait();
                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
                return result;
            }
        }

        /// <summary>Forwards Read/Dispose to the STA thread so mid-hash/copy reads never cross apartments either.</summary>
        private sealed class StaStream : Stream
        {
            private readonly Stream _inner;
            public StaStream(Stream inner) => _inner = inner;

            public override bool CanRead  => true;
            public override bool CanSeek  => false;
            public override bool CanWrite => false;
            public override long Length   => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                StaExecutor.Run(() => _inner.Read(buffer, offset, count));

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    StaExecutor.Run(() => { _inner.Dispose(); return true; });
                base.Dispose(disposing);
            }
        }

        private MediaDevice? _connectedDevice;
        private string? _connectedDeviceId;

        public static IEnumerable<MtpDeviceInfo> GetConnectedDevices() =>
            StaExecutor.Run(() =>
            {
                var result = new List<MtpDeviceInfo>();

                foreach (var device in MediaDevice.GetDevices())
                {
                    try
                    {
                        result.Add(new MtpDeviceInfo
                        {
                            DeviceId     = device.DeviceId,
                            FriendlyName = device.FriendlyName
                        });
                    }
                    catch
                    {
                        // Skip devices that fail to report identity (e.g. mid-enumeration disconnect)
                    }
                }

                return result;
            });

        // ── Everything below this point must only ever run on the STA thread ──

        private MediaDevice Connect(string deviceId)
        {
            if (_connectedDevice != null && _connectedDeviceId == deviceId && _connectedDevice.IsConnected)
                return _connectedDevice;

            return ForceReconnect(deviceId);
        }

        private MediaDevice ForceReconnect(string deviceId)
        {
            DisconnectCore();

            var device = MediaDevice.GetDevices().FirstOrDefault(d => d.DeviceId == deviceId)
                ?? throw new InvalidOperationException("The selected device is no longer connected.");

            // The cached instance may still report IsConnected=true from a session the
            // driver already closed on its own; force a clean close before reopening.
            try
            {
                if (device.IsConnected)
                    device.Disconnect();
            }
            catch
            {
                // best-effort cleanup
            }

            device.Connect(MediaDeviceAccess.GenericRead, MediaDeviceShare.Read);

            _connectedDevice   = device;
            _connectedDeviceId = deviceId;
            return device;
        }

        /// <summary>Runs a device operation on the STA thread, retrying exactly once with a forced clean reconnect if the cached session turns out to be stale.</summary>
        private T ExecuteWithRetry<T>(string deviceId, Func<MediaDevice, T> action) =>
            StaExecutor.Run(() =>
            {
                var device = Connect(deviceId);
                try
                {
                    return action(device);
                }
                catch (COMException)
                {
                    device = ForceReconnect(deviceId);
                    return action(device);
                }
            });

        /// <summary>
        /// Lists every file under the device root, walking one folder at a time. Runs
        /// synchronously — callers should wrap in Task.Run. A folder the device refuses to
        /// list (common for app folders) is added to skippedFolders and the walk carries on;
        /// a single all-at-once recursive enumeration would instead abort on the first such
        /// folder and silently return a partial list. Reports the running count every 20
        /// files and honours cancellation between items.
        /// </summary>
        public List<MtpFileEntry> EnumerateFilesRecursive(
            string deviceId,
            CancellationToken cancellationToken = default,
            IProgress<int>? progress = null,
            List<string>? skippedFolders = null) =>
            ExecuteWithRetry(deviceId, device =>
            {
                string root = device.GetRootDirectory().FullName;
                var entries = new List<MtpFileEntry>();
                var pending = new Stack<string>();
                pending.Push(root);

                string Relative(string path) =>
                    path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        ? path.Substring(root.Length).TrimStart('\\')
                        : path;

                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string folder = pending.Pop();

                    List<string> files, subfolders;
                    try
                    {
                        files      = device.EnumerateFiles(folder).ToList();
                        subfolders = device.EnumerateDirectories(folder).ToList();
                    }
                    catch (COMException) when (folder != root)
                    {
                        skippedFolders?.Add(Relative(folder));
                        continue;
                    }

                    foreach (var path in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        MediaFileInfo info;
                        try
                        {
                            info = device.GetFileInfo(path);
                        }
                        catch
                        {
                            continue; // file vanished or is inaccessible mid-enumeration
                        }

                        entries.Add(new MtpFileEntry
                        {
                            RelativePath  = Relative(path),
                            DevicePath    = path,
                            Length        = (long)info.Length,
                            LastWriteTime = info.LastWriteTime ?? DateTime.Now
                        });

                        if (entries.Count % 20 == 0)
                            progress?.Report(entries.Count);
                    }

                    for (int i = subfolders.Count - 1; i >= 0; i--)
                        pending.Push(subfolders[i]);
                }

                progress?.Report(entries.Count);
                return entries;
            });

        /// <summary>Opens a sequential read stream directly off the device for hashing/copying. All reads are routed back through the same STA thread that opened it.</summary>
        public Stream OpenRead(string deviceId, string devicePath) =>
            new StaStream(ExecuteWithRetry(deviceId, device => device.GetFileInfo(devicePath).OpenRead()));

        /// <summary>Re-queries live size/date from the device — used for tamper/modification checks in place of FileInfo.</summary>
        public (long length, DateTime lastWriteTime) GetCurrentMetadata(string deviceId, string devicePath) =>
            ExecuteWithRetry(deviceId, device =>
            {
                var info = device.GetFileInfo(devicePath);
                return ((long)info.Length, info.LastWriteTime ?? DateTime.Now);
            });

        public string GetFriendlyName(string deviceId) => StaExecutor.Run(() => Connect(deviceId).FriendlyName);

        public void Disconnect() => StaExecutor.Run(() =>
        {
            DisconnectCore();
            return true;
        });

        private void DisconnectCore()
        {
            if (_connectedDevice == null) return;

            try
            {
                if (_connectedDevice.IsConnected)
                    _connectedDevice.Disconnect();
            }
            catch
            {
                // best-effort cleanup
            }

            _connectedDevice   = null;
            _connectedDeviceId = null;
        }

        public void Dispose() => Disconnect();
    }
}
