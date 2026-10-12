using System;

namespace HashDash.Models
{
    public class ProcessLog
    {
        public int Id { get; set; }
        public string ExaminerName { get; set; } = "";
        public string LabCaseRef { get; set; } = "";
        public string ExhibitRef { get; set; } = "";
        public string ComputerName { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string SourceDriveLabel { get; set; } = "";
        public string DestinationPath { get; set; } = "";
        public string DestinationDriveLabel { get; set; } = "";
        public string SecondaryDestinationPath { get; set; } = "";
        public string SecondaryDestinationDriveLabel { get; set; } = "";
        public bool Md5Enabled { get; set; }
        public bool Sha256Enabled { get; set; }
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string Status { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public bool AutomationMode { get; set; }
        public string? PdfReportPath { get; set; }
        public DateTime CreatedAt { get; set; }

        // ✅ DISPLAY PROPERTIES
        public string TotalSizeDisplay => FormatBytes(TotalSizeBytes);
        
        public string DurationDisplay
        {
            get
            {
                var duration = EndDateTime - StartDateTime;
                if (duration.TotalHours >= 1)
                    return $"{duration.Hours}h {duration.Minutes}m";
                else if (duration.TotalMinutes >= 1)
                    return $"{duration.Minutes}m {duration.Seconds}s";
                else
                    return $"{duration.Seconds}s";
            }
        }

        public string ModeDisplay => AutomationMode ? "Automation" : "Manual";

        public string StartTimeDisplay => StartDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        
        public string EndTimeDisplay => EndDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    public class FileResult
    {
        public int Id { get; set; }
        public int ProcessLogId { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public string? Md5Hash { get; set; }
        public string? Sha256Hash { get; set; }
        public string? DestMd5Hash { get; set; }
        public string? DestSha256Hash { get; set; }
        public string? SecondaryDestMd5Hash { get; set; }
        public string? SecondaryDestSha256Hash { get; set; }
        public string? VerificationResult { get; set; }
        public string? VerificationStatus { get; set; }
    }
}