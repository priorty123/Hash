using HashDash.Models;
using System;
using System.IO;
using System.Linq;

namespace HashDash.Services
{
    public class SettingsService
    {
        public AppSettings Settings { get; private set; }

        public SettingsService()
        {
            Settings = AppSettings.Load();

            bool needsSave = false;

            // Default LogDirectory: {exe root}\logs — next to the exe so it's easy to find.
            if (string.IsNullOrWhiteSpace(Settings.LogDirectory))
            {
                Settings.LogDirectory = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "logs");
                needsSave = true;
            }

            Directory.CreateDirectory(Settings.LogDirectory);

            // Persist any defaults that were just applied so the file always exists after
            // first run and the user doesn't lose them on the next launch.
            if (needsSave)
            {
                try { Settings.Save(); }
                catch { /* non-fatal */ }
            }
        }

        public void SaveSettings()
        {
            Settings.Save();
        }

        /// <summary>
        /// Reloads settings from disk — call this after the SettingsWindow closes
        /// so that any changes made in the dialog take effect in the current instance.
        /// </summary>
        public void Reload()
        {
            Settings = AppSettings.Load();

            if (string.IsNullOrWhiteSpace(Settings.LogDirectory))
            {
                Settings.LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(Settings.LogDirectory);
                try { Settings.Save(); } catch { }
            }
        }

        public string GetLogDirectory()
        {
            if (!Directory.Exists(Settings.LogDirectory))
            {
                Directory.CreateDirectory(Settings.LogDirectory);
            }
            return Settings.LogDirectory;
        }

        public string GetAutoLogPath(string exhibitRef)
        {
            string logDir = GetLogDirectory();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeExhibitRef = string.IsNullOrWhiteSpace(exhibitRef) 
                ? "NoExhibit" 
                : exhibitRef.Replace(" ", "_");
            return Path.Combine(logDir, $"{safeExhibitRef}_{timestamp}.txt");
        }

        public void AddRecentPath(string path, bool isSource)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var list = isSource ? Settings.RecentSourcePaths : Settings.RecentDestinationPaths;

            // Remove if already exists
            list.Remove(path);

            // Add to front
            list.Insert(0, path);

            // Keep only MaxRecentPaths
            while (list.Count > Settings.MaxRecentPaths)
            {
                list.RemoveAt(list.Count - 1);
            }

            SaveSettings();
        }
    }
}