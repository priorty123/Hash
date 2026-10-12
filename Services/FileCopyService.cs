using HashDash.Helpers;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HashDash.Services
{
    public class FileCopyService
    {
        private const int ChunkSize = 1024 * 1024; // 1 MB chunks

        public event EventHandler<CopyProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Copies from any readable stream (e.g. an MTP device object stream) to a local
        /// destination file in 1MB chunks, reporting progress in the same shape as the
        /// CopyFile2-based method below. Used when the source isn't a real filesystem path
        /// and CopyFile2 therefore cannot read it directly.
        /// </summary>
        public async Task<CopyResult> CopyFromStreamWithProgressAsync(
            Func<Stream> openSourceStream,
            long totalBytes,
            string destinationPath,
            CancellationToken cancellationToken = default,
            IProgress<CopyProgressEventArgs>? progress = null)
        {
            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var sourceStream = openSourceStream();
                    using var destStream = new FileStream(
                        destinationPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: ChunkSize,
                        FileOptions.SequentialScan);

                    byte[] buffer      = new byte[ChunkSize];
                    long   transferred = 0;
                    int    bytesInChunk;

                    while ((bytesInChunk = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        destStream.Write(buffer, 0, bytesInChunk);
                        transferred += bytesInChunk;

                        var args = new CopyProgressEventArgs
                        {
                            TotalBytes       = totalBytes,
                            TransferredBytes = transferred,
                            Percentage       = totalBytes > 0 ? 100.0 * transferred / totalBytes : 0
                        };
                        progress?.Report(args);
                        OnProgressChanged(args);
                    }

                }, cancellationToken);

                return new CopyResult { Success = true };
            }
            catch (OperationCanceledException)
            {
                CleanupFailedCopy(destinationPath);
                return new CopyResult
                {
                    Success = false,
                    IsCancelled = true,
                    ErrorMessage = "Operation cancelled by user."
                };
            }
            catch (Exception ex)
            {
                CleanupFailedCopy(destinationPath);
                return new CopyResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Copies a file with progress reporting, maintaining forensic integrity.
        /// </summary>
        public async Task<CopyResult> CopyFileWithProgressAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default,
            IProgress<CopyProgressEventArgs>? progress = null)
        {
            // Allocate unmanaged memory for the cancellation boolean (BOOL is 32-bit int in Win32)
            IntPtr cancelPtr = Marshal.AllocHGlobal(sizeof(int));
            
            try
            {
                Marshal.WriteInt32(cancelPtr, 0); // 0 = False
                
                using var registration = cancellationToken.Register(() =>
                {
                    Marshal.WriteInt32(cancelPtr, 1); // 1 = True (Cancel requested)
                });

                // Run the unmanaged call on a background thread
                int hrResult = await Task.Run(() =>
                {
                    // MUST declare delegate here to keep it alive during unmanaged execution
                    NativeMethods.CopyFile2ProgressRoutine callbackDelegate = (ref NativeMethods.COPYFILE2_MESSAGE message, IntPtr context) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return NativeMethods.COPYFILE2_MESSAGE_ACTION.COPYFILE2_PROGRESS_CANCEL;
                        }

                        if (message.Type == NativeMethods.COPYFILE2_MESSAGE_TYPE.COPYFILE2_CALLBACK_CHUNK_FINISHED)
                        {
                            var info = message.Info.ChunkFinished;
                            if (info.liTotalFileSize > 0)
                            {
                                double percentage = 100.0 * info.liTotalBytesTransferred / info.liTotalFileSize;
                                var args = new CopyProgressEventArgs
                                {
                                    TotalBytes = info.liTotalFileSize,
                                    TransferredBytes = info.liTotalBytesTransferred,
                                    Percentage = percentage
                                };

                                // Support both modern IProgress and traditional Events
                                progress?.Report(args);
                                OnProgressChanged(args);
                            }
                        }
                        else if (message.Type == NativeMethods.COPYFILE2_MESSAGE_TYPE.COPYFILE2_CALLBACK_ERROR)
                        {
                            var error = message.Info.Error;
                            System.Diagnostics.Debug.WriteLine($"CopyFile2 error in phase {error.CopyPhase}: HRESULT {error.hrFailure}");
                        }

                        return NativeMethods.COPYFILE2_MESSAGE_ACTION.COPYFILE2_PROGRESS_CONTINUE;
                    };

                    var extendedParams = new NativeMethods.COPYFILE2_EXTENDED_PARAMETERS
                    {
                        dwSize = (uint)Marshal.SizeOf<NativeMethods.COPYFILE2_EXTENDED_PARAMETERS>(),
                        // REMOVED: COPY_FILE_NO_BUFFERING (Causes Error 87 on unaligned file sizes)
                        // ADDED: COPY_FILE_COPY_SYMLINK (Crucial for forensics to not follow symlinks)
                        dwCopyFlags = NativeMethods.COPY_FILE_RESTARTABLE | 
                                      NativeMethods.COPY_FILE_COPY_SYMLINK,
                        pfCancel = cancelPtr,
                        pProgressRoutine = callbackDelegate,
                        pvCallbackContext = IntPtr.Zero
                    };

                    int result = NativeMethods.CopyFile2(
                        sourcePath,
                        destinationPath,
                        ref extendedParams);

                    // GC.KeepAlive is critical to prevent the garbage collector from 
                    // destroying the delegate while CopyFile2 is still executing.
                    GC.KeepAlive(callbackDelegate);

                    return result;

                }, cancellationToken);

                // Analyze HRESULT
                if (hrResult == NativeMethods.S_OK)
                {
                    return new CopyResult { Success = true };
                }

                // Handle Failure States
                CleanupFailedCopy(destinationPath);

                if (cancellationToken.IsCancellationRequested || hrResult == NativeMethods.E_ABORT)
                {
                    return new CopyResult
                    {
                        Success = false,
                        IsCancelled = true,
                        ErrorMessage = "Operation cancelled by user."
                    };
                }

                // Extract standard Exception from HRESULT for accurate error message
                Exception? hrException = Marshal.GetExceptionForHR(hrResult);
                
                return new CopyResult
                {
                    Success = false,
                    ErrorCode = hrResult,
                    ErrorMessage = hrException?.Message ?? $"Unknown HRESULT error: {hrResult:X}"
                };
            }
            finally
            {
                Marshal.FreeHGlobal(cancelPtr);
            }
        }

        /// <summary>
        /// Simple copy fallback for zero-byte files or when progress isn't needed.
        /// </summary>
        public async Task<CopyResult> CopyFileAsync(
            string sourcePath,
            string destinationPath,
            bool overwrite = false)
        {
            try
            {
                await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite));
                return new CopyResult { Success = true };
            }
            catch (Exception ex)
            {
                return new CopyResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }


        private void CleanupFailedCopy(string destinationPath)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clean up partial file: {ex.Message}");
            }
        }

        protected virtual void OnProgressChanged(CopyProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }
    }

    public class CopyProgressEventArgs : EventArgs
    {
        public long TotalBytes { get; set; }
        public long TransferredBytes { get; set; }
        public double Percentage { get; set; }
    }

    public class CopyResult
    {
        public bool Success { get; set; }
        public bool IsCancelled { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }





}