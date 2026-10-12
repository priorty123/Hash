using HashDash.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HashDash.Services
{
    public class FileIntegrityService
    {
        private readonly Dictionary<string, FileMetadata> _preOperationMetadata = new();

        /// <summary>
        /// Capture file metadata before processing
        /// </summary>
        public void CaptureMetadata(string filePath)
        {
            if (File.Exists(filePath))
            {
                _preOperationMetadata[filePath] = FileMetadata.Capture(filePath);
            }
        }

        /// <summary>
        /// Verify file hasn't been modified since capture
        /// </summary>
        public bool VerifyIntegrity(string filePath)
        {
            if (!_preOperationMetadata.ContainsKey(filePath))
                return true; // No baseline = assume OK

            return !_preOperationMetadata[filePath].IsModified();
        }

        /// <summary>
        /// Get integrity report for all tracked files
        /// </summary>
        public Dictionary<string, bool> GetIntegrityReport()
        {
            return _preOperationMetadata.ToDictionary(
                kvp => kvp.Key,
                kvp => !kvp.Value.IsModified());
        }

        /// <summary>
        /// Clear all tracked metadata
        /// </summary>
        public void Clear()
        {
            _preOperationMetadata.Clear();
        }
    }
}