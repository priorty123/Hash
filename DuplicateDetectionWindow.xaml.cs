using HashDash.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace HashDash
{
    public partial class DuplicateDetectionWindow : Window
    {
        private readonly Dictionary<string, List<FileHashEntry>> _duplicates;

        public DuplicateDetectionWindow(Dictionary<string, List<FileHashEntry>> duplicates)
        {
            InitializeComponent();

            _duplicates = duplicates;

            // Create display items
            var displayItems = duplicates.Select(kvp => new DuplicateDisplayItem
            {
                Hash = kvp.Key,
                Count = kvp.Value.Count,
                TotalSize = kvp.Value.Sum(f => f.FileSizeBytes),
                FilePaths = kvp.Value.Select(f => f.FullPath).ToList()
            }).OrderByDescending(d => d.Count).ToList();

            dgDuplicates.ItemsSource = displayItems;

            // Update summary
            int totalDuplicates = duplicates.Sum(kvp => kvp.Value.Count);
            long totalWastedSpace = duplicates.Sum(kvp => kvp.Value.Sum(f => f.FileSizeBytes) - kvp.Value.First().FileSizeBytes);

            txtSummary.Text = $"Found {duplicates.Count} duplicate hash groups ({totalDuplicates} total files). " +
                              $"Potential space saving: {FormatBytes(totalWastedSpace)}";
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "Text File (*.txt)|*.txt|CSV File (*.csv)|*.csv",
                    FileName = $"Duplicates_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".txt"
                };

                if (dlg.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("DUPLICATE FILE DETECTION REPORT");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Total duplicate groups: {_duplicates.Count}");
                sb.AppendLine();

                foreach (var kvp in _duplicates.OrderByDescending(x => x.Value.Count))
                {
                    sb.AppendLine($"Hash: {kvp.Key}");
                    sb.AppendLine($"Count: {kvp.Value.Count} files");
                    sb.AppendLine($"Total Size: {FormatBytes(kvp.Value.Sum(f => f.FileSizeBytes))}");
                    sb.AppendLine("Files:");
                    foreach (var file in kvp.Value)
                    {
                        sb.AppendLine($"  - {file.FullPath} ({FormatBytes(file.FileSizeBytes)})");
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show($"Report saved to:\n{dlg.FileName}", "Export Successful",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting report: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    

    // Helper class for DataGrid binding
    public class DuplicateDisplayItem
    {
        public string Hash { get; set; } = "";
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public List<string> FilePaths { get; set; } = new();

        public string TotalSizeDisplay => FormatBytes(TotalSize);
        public string FilePathsDisplay => string.Join(" | ", FilePaths.Select(Path.GetFileName));

    }


    }
}