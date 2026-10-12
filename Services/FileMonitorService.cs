using HashDash.Models;
using System;
using System.IO;

namespace HashDash.Services
{
    public class FileMonitorService
    {
        private FileSystemWatcher? _watcher;
        private AutomationConfig _config;
        private readonly object _lockObject = new();

        public event EventHandler<string>? FolderDetected;

        public FileMonitorService(AutomationConfig config)
        {
            _config = config;
        }

        public void UpdateConfig(AutomationConfig config)
        {
            lock (_lockObject)
            {
                bool wasRunning = _watcher != null;
                
                if (wasRunning)
                {
                    Stop();
                }

                _config = config;

                if (wasRunning && config.IsEnabled)
                {
                    Start();
                }
            }
        }

        public void Start()
        {
            lock (_lockObject)
            {
                if (_watcher != null)
                {
                    Stop();
                }

                if (string.IsNullOrWhiteSpace(_config.WatchFolder))
                {
                    throw new ArgumentException("Watch folder is not configured.");
                }

                if (!Directory.Exists(_config.WatchFolder))
                {
                    throw new DirectoryNotFoundException($"Watch folder does not exist: {_config.WatchFolder}");
                }

                if (string.IsNullOrWhiteSpace(_config.TriggerFilePattern))
                {
                    throw new ArgumentException("Trigger file pattern is not configured.");
                }

                _watcher = new FileSystemWatcher
                {
                    Path = _config.WatchFolder,
                    Filter = _config.TriggerFilePattern,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = _config.IncludeSubdirectories,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnTriggerFileCreated;
            }
        }

        public void Stop()
        {
            lock (_lockObject)
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnTriggerFileCreated;
                    _watcher.Dispose();
                    _watcher = null;
                }
            }
        }

        private void OnTriggerFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Get the folder containing the trigger file
                string? folderPath = Path.GetDirectoryName(e.FullPath);
                
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    // Brief delay to ensure file is fully written
                    System.Threading.Thread.Sleep(500);

                    // Raise event with the folder path
                    FolderDetected?.Invoke(this, folderPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing trigger file: {ex.Message}");
            }
        }
    }
}