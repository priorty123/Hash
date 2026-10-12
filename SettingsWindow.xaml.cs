using HashDash.Models;
using HashDash.Services;
using HashDash.ViewModels;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace HashDash
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly MainViewModel? _mainViewModel;
        private AutomationConfig? _automationConfig;

        /// <summary>
        /// Opens the settings window using the application's shared SettingsService instance.
        /// Passing the shared instance avoids two-instance desync where the window saves to
        /// disk but the running app keeps an old in-memory value.
        /// </summary>
        public SettingsWindow(SettingsService? sharedSettingsService = null)
        {
            InitializeComponent();

            // Prefer the shared instance; fall back to a new one only if called standalone.
            _settingsService = sharedSettingsService ?? new SettingsService();

            // Get MainViewModel reference to check automation state.
            if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
                _mainViewModel = vm;

            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Settings;

            // Load database path
            if (!string.IsNullOrWhiteSpace(settings.DatabasePath))
            {
                txtDatabasePath.Text = settings.DatabasePath;
            }
            else
            {
                // ✅ Set default to Logs subfolder in app directory
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string logsFolder = Path.Combine(appDirectory, "Logs");
                Directory.CreateDirectory(logsFolder);
                txtDatabasePath.Text = Path.Combine(logsFolder, "ProcessLog.db");
            }

            // Load automation settings
            txtTriggerPattern.Text = string.IsNullOrWhiteSpace(settings.TriggerFilePattern)
                ? "ExtractionSummary.pdf"
                : settings.TriggerFilePattern;
            chkIncludeSubdirs.IsChecked = settings.IncludeSubdirectories;
            chkDelayEnabled.IsChecked   = settings.AutomationDelayEnabled;
            txtDelayMinutes.Text        = settings.AutomationDelayMinutes.ToString();

            // Load CaseTrace integration settings
            txtLogDirectory.Text = settings.LogDirectory;
        }

        private void BtnBrowseLogDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Select CaseTrace Log Folder",
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(txtLogDirectory.Text) && Directory.Exists(txtLogDirectory.Text))
                    dlg.InitialDirectory = txtLogDirectory.Text;

                if (dlg.ShowDialog() == true)
                    txtLogDirectory.Text = dlg.FolderName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBrowseDatabase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                    FileName = "ProcessLog.db",
                    Title = "Select Database Location",
                    DefaultExt = ".db"
                };

                if (!string.IsNullOrWhiteSpace(txtDatabasePath.Text))
                {
                    string? directory = Path.GetDirectoryName(txtDatabasePath.Text);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        dlg.InitialDirectory = directory;
                    }
                }

                if (dlg.ShowDialog() == true)
                {
                    txtDatabasePath.Text = dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting database location: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Validate database path
            if (string.IsNullOrWhiteSpace(txtDatabasePath.Text))
            {
                MessageBox.Show("Please specify a database location.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtDatabasePath.Focus();
                return;
            }

            // Validate trigger pattern
            if (string.IsNullOrWhiteSpace(txtTriggerPattern.Text))
            {
                MessageBox.Show("Please specify a trigger file pattern.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtTriggerPattern.Focus();
                return;
            }

            // Validate delay minutes
            if (chkDelayEnabled.IsChecked == true)
            {
                if (!int.TryParse(txtDelayMinutes.Text.Trim(), out int delayVal) || delayVal < 0 || delayVal > 60)
                {
                    MessageBox.Show("Please enter a valid delay between 0 and 60 minutes.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtDelayMinutes.Focus();
                    return;
                }
            }

            // ✅ FIX: Check if trigger pattern changed while automation is active
            bool triggerPatternChanged = _settingsService.Settings.TriggerFilePattern != txtTriggerPattern.Text;
            
            if (triggerPatternChanged && _mainViewModel?.AutomationEnabled == true)
            {
                var result = MessageBox.Show(
                    "Changing the trigger filename requires restarting automation.\n\n" +
                    "Automation will be disabled and you'll need to re-enable it.\n\n" +
                    "Continue?",
                    "Automation Will Be Disabled",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
                
                // ✅ Stop monitoring and disable automation before changing settings
                try
                {
                    // This will call StopMonitoring internally
                    _mainViewModel.AutomationEnabled = false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disabling automation: {ex.Message}");
                }
            }

            // Save settings
            try
            {
                var settings = _settingsService.Settings;
                settings.DatabasePath           = txtDatabasePath.Text;
                settings.TriggerFilePattern     = txtTriggerPattern.Text;
                settings.IncludeSubdirectories  = chkIncludeSubdirs.IsChecked ?? true;
                settings.AutomationDelayEnabled = chkDelayEnabled.IsChecked   ?? true;
                settings.AutomationDelayMinutes = int.TryParse(txtDelayMinutes.Text.Trim(), out int dm) ? dm : 2;
                settings.LogDirectory           = txtLogDirectory.Text.Trim();

                _settingsService.SaveSettings();

                // ✅ FIX: Update MainViewModel's trigger pattern immediately
                if (_mainViewModel != null)
                {
                    _mainViewModel.TriggerFilePattern = txtTriggerPattern.Text;
                }

                // Store automation config for retrieval (no watch folder - uses source from main window)
                _automationConfig = new AutomationConfig
                {
                    WatchFolder = "", // ✅ Will be set from main window source path
                    TriggerFilePattern = txtTriggerPattern.Text,
                    IncludeSubdirectories = chkIncludeSubdirs.IsChecked ?? true
                };

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public AutomationConfig GetAutomationConfig()
        {
            return _automationConfig ?? new AutomationConfig
            {
                WatchFolder           = "",
                TriggerFilePattern    = txtTriggerPattern.Text,
                IncludeSubdirectories = chkIncludeSubdirs.IsChecked ?? true,
                DelayEnabled          = chkDelayEnabled.IsChecked   ?? true,
                DelayMinutes          = int.TryParse(txtDelayMinutes.Text.Trim(), out int dm) ? dm : 2
            };
        }
    }
}