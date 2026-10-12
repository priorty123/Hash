using System;

namespace HashDash.Models
{
    /// <summary>
    /// Audit trail entry for chain of custody logging
    /// </summary>
    public class ChainOfCustodyEntry
    {
        public DateTime Timestamp { get; set; }
        public string Username { get; set; } = "";
        public string ComputerName { get; set; } = "";
        public string Action { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string Hash { get; set; } = "";
        public string Details { get; set; } = "";

        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {ComputerName}\\{Username}: {Action} - {FilePath}";
        }
    }
}