using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HashDash.Services
{
    /// <summary>
    /// Reports bytes processed so far for a single file hash operation.
    /// </summary>
    public class HashProgressEventArgs
    {
        public long BytesProcessed { get; init; }
        public long TotalBytes     { get; init; }
        public double PercentComplete => TotalBytes > 0
            ? 100.0 * BytesProcessed / TotalBytes
            : 0;
    }

    public class HashService
    {
        private const int ChunkSize = 1024 * 1024; // 1 MB chunks

        /// <summary>
        /// Computes MD5 and/or SHA-256 hashes for a local file in 1MB chunks,
        /// reporting progress after every chunk via the optional callback.
        /// </summary>
        public async Task<(string md5, string sha256)> ComputeHashPairAsync(
            string filePath,
            bool calculateMd5,
            bool calculateSha256,
            CancellationToken cancellationToken = default,
            IProgress<HashProgressEventArgs>? progress = null)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(filePath))
                    throw new FileNotFoundException(
                        $"Cannot hash file - file not found: {filePath}", filePath);

                long fileSize = new FileInfo(filePath).Length;

                using var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: ChunkSize,
                    FileOptions.SequentialScan);

                return ComputeHashPairFromStream(
                    fileStream, fileSize, calculateMd5, calculateSha256, cancellationToken, progress);

            }, cancellationToken);
        }

        /// <summary>
        /// Computes MD5 and/or SHA-256 hashes from any readable stream (e.g. an MTP device
        /// object stream) in the same 1MB-chunk pattern as the filesystem overload above.
        /// The stream is opened lazily inside the background task so device I/O never
        /// blocks the calling thread.
        /// </summary>
        public async Task<(string md5, string sha256)> ComputeHashPairAsync(
            Func<Stream> openStream,
            long length,
            bool calculateMd5,
            bool calculateSha256,
            CancellationToken cancellationToken = default,
            IProgress<HashProgressEventArgs>? progress = null)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = openStream();

                return ComputeHashPairFromStream(
                    stream, length, calculateMd5, calculateSha256, cancellationToken, progress);

            }, cancellationToken);
        }

        private static (string md5, string sha256) ComputeHashPairFromStream(
            Stream stream,
            long length,
            bool calculateMd5,
            bool calculateSha256,
            CancellationToken cancellationToken,
            IProgress<HashProgressEventArgs>? progress)
        {
            using MD5?    md5    = calculateMd5    ? MD5.Create()    : null;
            using SHA256? sha256 = calculateSha256 ? SHA256.Create() : null;

            byte[] buffer    = new byte[ChunkSize];
            long   bytesRead = 0;
            int    bytesInChunk;

            while ((bytesInChunk = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                md5?.TransformBlock(buffer, 0, bytesInChunk, null, 0);
                sha256?.TransformBlock(buffer, 0, bytesInChunk, null, 0);

                bytesRead += bytesInChunk;

                progress?.Report(new HashProgressEventArgs
                {
                    BytesProcessed = bytesRead,
                    TotalBytes     = length
                });
            }

            // Finalize unconditionally (zero-length final block) rather than guessing
            // which read was "the last chunk" — correct regardless of whether the
            // reported length matches the stream's actual byte count exactly.
            md5?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            sha256?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            string md5Result    = md5?.Hash    != null
                ? BitConverter.ToString(md5.Hash).Replace("-", "")
                : "";
            string sha256Result = sha256?.Hash != null
                ? BitConverter.ToString(sha256.Hash).Replace("-", "")
                : "";

            return (md5Result, sha256Result);
        }

        /// <summary>Computes only MD5 hash.</summary>
        public async Task<string> ComputeMd5Async(
            string filePath,
            CancellationToken cancellationToken = default,
            IProgress<HashProgressEventArgs>? progress = null)
        {
            var (md5, _) = await ComputeHashPairAsync(
                filePath, true, false, cancellationToken, progress);
            return md5;
        }

        /// <summary>Computes only SHA-256 hash.</summary>
        public async Task<string> ComputeSha256Async(
            string filePath,
            CancellationToken cancellationToken = default,
            IProgress<HashProgressEventArgs>? progress = null)
        {
            var (_, sha256) = await ComputeHashPairAsync(
                filePath, false, true, cancellationToken, progress);
            return sha256;
        }
    }
}
