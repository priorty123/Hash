using HashDash.Models;
using HashDash.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace HashDash
{
    public partial class LogsWindow : Window
    {
        private readonly DatabaseService _databaseService;
        private List<ProcessLog> _allLogs         = new();
        private List<ProcessLog> _dateFilteredLogs = new();
        private string           _searchText       = "";

        public LogsWindow(DatabaseService databaseService)
        {
            InitializeComponent();
            _databaseService = databaseService;

            dpEndDate.SelectedDate   = DateTime.Now;
            dpStartDate.SelectedDate = DateTime.Now.AddDays(-30);

            LoadLogs();
        }

        private void LoadLogs()
        {
            try
            {
                _allLogs         = _databaseService.GetAllProcessLogs();
                _dateFilteredLogs = _allLogs;
                ApplySearchFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading logs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplySearchFilter()
        {
            var source = _dateFilteredLogs;
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var term = _searchText.Trim();
                source = source.Where(log =>
                    Contains(log.ExaminerName,          term) ||
                    Contains(log.LabCaseRef,             term) ||
                    Contains(log.ExhibitRef,             term) ||
                    Contains(log.ComputerName,           term) ||
                    Contains(log.SourceDriveLabel,       term) ||
                    Contains(log.DestinationDriveLabel,  term) ||
                    Contains(log.Status,                 term) ||
                    Contains(log.StartTimeDisplay,       term)
                ).ToList();
            }
            dgLogs.ItemsSource = source;
            UpdateRecordCount();
        }

        private static bool Contains(string? source, string term) =>
            source?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _searchText = txtSearch.Text;
            ApplySearchFilter();
        }

        private void UpdateRecordCount()
        {
            int count = dgLogs.Items.Count;
            txtRecordCount.Text = $"Showing {count:N0} record{(count == 1 ? "" : "s")}";
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            if (dpStartDate.SelectedDate == null || dpEndDate.SelectedDate == null)
            {
                MessageBox.Show("Please select both start and end dates.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var startDate = dpStartDate.SelectedDate.Value.Date;
                var endDate   = dpEndDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);
                _dateFilteredLogs = _databaseService.GetProcessLogsByDateRange(startDate, endDate);
                ApplySearchFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error filtering logs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            dpStartDate.SelectedDate = DateTime.Now.AddDays(-30);
            dpEndDate.SelectedDate   = DateTime.Now;
            _dateFilteredLogs        = _allLogs;
            ApplySearchFilter();
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var logs = dgLogs.ItemsSource as List<ProcessLog>;
        if (logs == null || logs.Count == 0)
        {
            MessageBox.Show("No data to export.", "No Data",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV File (*.csv)|*.csv",
            FileName = $"ProcessLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            DefaultExt = ".csv"
        };

        if (dlg.ShowDialog() != true) return;

        var csv = new StringBuilder();
        csv.AppendLine("Start Date/Time,End Date/Time,Examiner,Case Ref,Exhibit Ref,Computer,Source Label,Dest Label,Files,Total Size,Duration,Status,Mode");

        foreach (var log in logs)
        {
            csv.AppendLine($"\"{log.StartTimeDisplay}\"," +
                          $"\"{log.EndTimeDisplay}\"," +
                          $"\"{log.ExaminerName}\"," +
                          $"\"{log.LabCaseRef}\"," +
                          $"\"{log.ExhibitRef}\"," +
                          $"\"{log.ComputerName}\"," +
                          $"\"{log.SourceDriveLabel}\"," +
                          $"\"{log.DestinationDriveLabel}\"," +
                          $"{log.FileCount}," +
                          $"\"{log.TotalSizeDisplay}\"," +
                          $"\"{log.DurationDisplay}\"," +
                          $"\"{log.Status}\"," +
                          $"\"{log.ModeDisplay}\"");
        }

        File.WriteAllText(dlg.FileName, csv.ToString(), Encoding.UTF8);

        MessageBox.Show($"Log exported to:\n{dlg.FileName}", "Export Successful",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error exporting CSV: {ex.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}