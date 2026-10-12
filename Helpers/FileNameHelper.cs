using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HashDash.Helpers
{
    public static class FileNameHelper
    {
        /// <summary>
        /// Sanitizes a filename by removing invalid characters and replacing whitespace with underscores
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            return Regex.Replace(clean, @"\s+", "_");
        }

        /// <summary>
        /// Escapes a string for CSV by wrapping in quotes and escaping existing quotes
        /// </summary>
        public static string EscapeCsv(string? value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("\"", "\"\"");
        }

        /// <summary>
        /// Generates a timestamped filename with optional prefix
        /// </summary>
        public static string GenerateTimestampedName(string prefix = "File", string extension = "txt")
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{Sanitize(prefix)}_{timestamp}.{extension.TrimStart('.')}";
        }
    }
}