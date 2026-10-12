using System;
using System.IO;
using System.Text;

namespace HashDash.Services
{
    public class DetailedLogService
    {
        private readonly StringBuilder _logBuilder;
        private readonly object _lockObject = new();
        private string? _currentLogPath;

        public DetailedLogService()
        {
            _logBuilder = new StringBuilder();
        }

        /// <summary>
        /// Start a new log session
        /// </summary>
        public void StartNewLog(string logDirectory, string exhibitRef, string caseRef)
        {
            lock (_lockObject)
            {
                _logBuilder.Clear();
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeExhibitRef = exhibitRef.Replace(" ", "").Replace("/", "-").Replace("\\", "-");
                string safeCaseRef = caseRef.Replace(" ", "").Replace("/", "-").Replace("\\", "-");
                string fileName = $"{safeExhibitRef}-{safeCaseRef}_{timestamp}_DetailedLog.txt";
                
                _currentLogPath = Path.Combine(logDirectory, fileName);
                
                LogHeader(exhibitRef, caseRef);
            }
        }

        /// <summary>
        /// Log header information
        /// </summary>
        private void LogHeader(string exhibitRef, string caseRef)
        {
            _logBuilder.AppendLine("═══════════════════════════════════════════════════════════════════");
            _logBuilder.AppendLine("                    HASHDASH v2.0 - DETAILED LOG");
            _logBuilder.AppendLine("═══════════════════════════════════════════════════════════════════");
            _logBuilder.AppendLine($"Exhibit Reference : {exhibitRef}");
            _logBuilder.AppendLine($"Case Reference    : {caseRef}");
            _logBuilder.AppendLine($"Log Started       : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logBuilder.AppendLine($"Computer Name     : {Environment.MachineName}");
            _logBuilder.AppendLine("═══════════════════════════════════════════════════════════════════");
            _logBuilder.AppendLine();
        }

        /// <summary>
        /// Log an event with timestamp
        /// </summary>
        public void LogEvent(string module, string action, string details = "")
        {
            lock (_lockObject)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                _logBuilder.AppendLine($"[{timestamp}] [{module}] {action}");
                
                if (!string.IsNullOrWhiteSpace(details))
                {
                    _logBuilder.AppendLine($"  Details: {details}");
                }
            }
        }

        /// <summary>
        /// Log file processing start
        /// </summary>
        public void LogFileStart(string fileName, int fileNumber, int totalFiles)
        {
            lock (_lockObject)
            {
                _logBuilder.AppendLine();
                _logBuilder.AppendLine($"─────────────────────────────────────────────────────────────────");
                _logBuilder.AppendLine($"Processing File {fileNumber}/{totalFiles}: {fileName}");
                _logBuilder.AppendLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            }
        }

        /// <summary>
        /// Log file processing end
        /// </summary>
        public void LogFileEnd(string fileName, string status, TimeSpan duration)
        {
            lock (_lockObject)
            {
                _logBuilder.AppendLine($"Status: {status}");
                _logBuilder.AppendLine($"Duration: {duration.TotalSeconds:F2} seconds");
                _logBuilder.AppendLine($"Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            }
        }

        /// <summary>
        /// Log hash calculation
        /// </summary>
        public void LogHash(string algorithm, string hash, TimeSpan duration)
        {
            lock (_lockObject)
            {
                _logBuilder.AppendLine($"  {algorithm}: {hash} (took {duration.TotalMilliseconds:F0}ms)");
            }
        }

        /// <summary>
        /// Log copy operation
        /// </summary>
        public void LogCopy(string source, string destination, long bytes, TimeSpan duration)
        {
            lock (_lockObject)
            {
                double mbps = (bytes / (1024.0 * 1024.0)) / duration.TotalSeconds;
                _logBuilder.AppendLine($"  Copy: {bytes:N0} bytes transferred in {duration.TotalSeconds:F2}s ({mbps:F2} MB/s)");
                _logBuilder.AppendLine($"  Source: {source}");
                _logBuilder.AppendLine($"  Destination: {destination}");
            }
        }

        /// <summary>
        /// Log verification result
        /// </summary>
        public void LogVerification(string result, bool md5Match, bool sha256Match)
        {
            lock (_lockObject)
            {
                _logBuilder.AppendLine($"  Verification: {result}");
                if (md5Match && sha256Match)
                {
                    _logBuilder.AppendLine($"  ✓ All hashes match");
                }
                else
                {
                    _logBuilder.AppendLine($"  ✗ HASH MISMATCH DETECTED");
                    if (!md5Match) _logBuilder.AppendLine($"    - MD5 mismatch");
                    if (!sha256Match) _logBuilder.AppendLine($"    - SHA-256 mismatch");
                }
            }
        }

        /// <summary>
        /// Log process summary
        /// </summary>
        public void LogSummary(int totalFiles, int successful, int failed, TimeSpan totalDuration)
        {
            lock (_lockObject)
            {
                _logBuilder.AppendLine();
                _logBuilder.AppendLine("═══════════════════════════════════════════════════════════════════");
                _logBuilder.AppendLine("                         PROCESS SUMMARY");
                _logBuilder.AppendLine("═══════════════════════════════════════════════════════════════════");
                _logBuilder.AppendLine($"Total Files Processed : {totalFiles}");
                _logBuilder.AppendLine($"Successful            : {successful}");
                _logBuilder.AppendLine($"Failed                : {failed}");
                _logBuilder.AppendLine($"Total Duration        : {totalDuration.Hours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}");
                _logBuilder.AppendLine($"Completed At          : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _logBuilder.AppendLine("═══════════════════════════════════════════════════════════════════");
            }
        }

        /// <summary>
        /// Save log to file
        /// </summary>
        public string SaveLog()
        {
            lock (_lockObject)
            {
                if (string.IsNullOrWhiteSpace(_currentLogPath))
                {
                    throw new InvalidOperationException("No log session started. Call StartNewLog first.");
                }

                string? directory = Path.GetDirectoryName(_currentLogPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_currentLogPath, _logBuilder.ToString(), Encoding.UTF8);
                return _currentLogPath;
            }
        }

        /// <summary>
        /// Get current log content
        /// </summary>
        public string GetLogContent()
        {
            lock (_lockObject)
            {
                return _logBuilder.ToString();
            }
        }
    }
}