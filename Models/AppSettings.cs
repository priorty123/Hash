using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HashDash.Models
{
    public class AppSettings
    {
        // ── Settings file location ────────────────────────────────────────────
        // Canonical home: %APPDATA%\HashDash\settings.json
        // This path is outside the project output folder so it survives every rebuild.
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HashDash",
            "settings.json");

        // Previous iterations stored the file in different places.
        // Load() checks all of them in order and migrates to SettingsFilePath on first find.
        private static readonly string[] LegacyPaths =
        [
            // v3 attempt — %LOCALAPPDATA%\HashDash\settings.json
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "HashDash", "settings.json"),
            // v1 original — next to the exe (wiped on rebuild)
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)
                         ?? AppDomain.CurrentDomain.BaseDirectory,
                         "HashDash_settings.json"),
        ];

        public bool Md5Enabled { get; set; } = true;
        public bool Sha256Enabled { get; set; } = true;
        public List<string> RecentSourcePaths { get; set; } = new();
        public List<string> RecentDestinationPaths { get; set; } = new();
        public string DatabasePath { get; set; } = "";
        public string WatchFolder { get; set; } = "";
        public string TriggerFilePattern { get; set; } = "ExtractionSummary.pdf";
        public bool IncludeSubdirectories { get; set; } = true;
        public int  AutomationDelayMinutes { get; set; } = 2;
        public bool AutomationDelayEnabled { get; set; } = true;

        // ✅ ADD THESE:
        public string LogDirectory { get; set; } = "";
        public int MaxRecentPaths { get; set; } = 10;
        public List<string> HiddenColumns { get; set; } = new();
        public bool ColumnVisibilityConfigured { get; set; } = false;
        public Dictionary<string, double> ColumnWidths { get; set; } = new();

        /// <summary>
        /// Loads settings from the canonical location (%APPDATA%\HashDash\settings.json).
        /// If not found, walks the legacy paths in order, migrates the first one it finds,
        /// and saves the result to the canonical path so future loads are instant.
        /// </summary>
        public static AppSettings Load()
        {
            // 1. Try canonical location.
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HashDash] Load failed: {ex.Message}");
                }
            }

            // 2. Migration: walk legacy paths and promote the first one found.
            foreach (string legacyPath in LegacyPaths)
            {
                if (!File.Exists(legacyPath)) continue;
                try
                {
                    string json = File.ReadAllText(legacyPath);
                    var migrated = JsonSerializer.Deserialize<AppSettings>(json);
                    if (migrated != null)
                    {
                        migrated.Save();   // write to canonical path
                        System.Diagnostics.Debug.WriteLine(
                            $"[HashDash] Migrated settings from {legacyPath}");
                        return migrated;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HashDash] Migration failed: {ex.Message}");
                }
            }

            // 3. First-ever run — caller will set defaults and save.
            return new AppSettings();
        }

        /// <summary>
        /// Save settings to JSON file
        /// </summary>
        public void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                throw;
            }
        }
    }
}