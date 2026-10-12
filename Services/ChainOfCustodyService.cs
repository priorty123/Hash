using HashDash.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HashDash.Services
{
    public class ChainOfCustodyService
    {
        private readonly List<ChainOfCustodyEntry> _entries = new();
        private readonly string _username;
        private readonly string _computerName;

        public ChainOfCustodyService()
        {
            _username = Environment.UserName;
            _computerName = Environment.MachineName;
        }

        public IReadOnlyList<ChainOfCustodyEntry> Entries => _entries.AsReadOnly();

        /// <summary>
        /// Log an action in the chain of custody
        /// </summary>
        public void LogAction(string action, string filePath, string hash = "", string details = "")
        {
            _entries.Add(new ChainOfCustodyEntry
            {
                Timestamp = DateTime.Now,
                Username = _username,
                ComputerName = _computerName,
                Action = action,
                FilePath = filePath,
                Hash = hash,
                Details = details
            });
        }

        /// <summary>
        /// Generate chain of custody report
        /// </summary>
        public string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine("  CHAIN OF CUSTODY LOG");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Entries: {_entries.Count}");
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────────────");
            sb.AppendLine();

            foreach (var entry in _entries)
            {
                sb.AppendLine($"Timestamp:    {entry.Timestamp:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"User:         {entry.ComputerName}\\{entry.Username}");
                sb.AppendLine($"Action:       {entry.Action}");
                sb.AppendLine($"File:         {entry.FilePath}");
                if (!string.IsNullOrEmpty(entry.Hash))
                    sb.AppendLine($"Hash:         {entry.Hash}");
                if (!string.IsNullOrEmpty(entry.Details))
                    sb.AppendLine($"Details:      {entry.Details}");
                sb.AppendLine();
                sb.AppendLine("───────────────────────────────────────────────────────────────────────");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Save chain of custody log to file
        /// </summary>
        public void SaveToFile(string filePath)
        {
            File.WriteAllText(filePath, GenerateReport(), Encoding.UTF8);
        }

        /// <summary>
        /// Clear all entries
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}