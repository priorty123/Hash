using System;
using System.IO;

namespace HashDash.Models
{
    public class FileMetadata
    {
        public string FilePath { get; set; } = "";
        public long Size { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime CaptureTime { get; set; }

        /// <summary>
        /// Capture current metadata of a file
        /// </summary>
        public static FileMetadata Capture(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var fileInfo = new FileInfo(filePath);

            return new FileMetadata
            {
                FilePath = filePath,
                Size = fileInfo.Length,
                LastWriteTime = fileInfo.LastWriteTime,
                CaptureTime = DateTime.Now
            };
        }

        /// <summary>
        /// Check if file has been modified since capture
        /// </summary>
        public bool IsModified()
        {
            if (!File.Exists(FilePath))
            {
                return true; // File deleted = modified
            }

            try
            {
                var fileInfo = new FileInfo(FilePath);

                // Check if size or last write time has changed
                return fileInfo.Length != Size || 
                       fileInfo.LastWriteTime != LastWriteTime;
            }
            catch
            {
                return true; // Error accessing file = assume modified
            }
        }
    }
}