using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HashDash
{
    public partial class ChangeLogWindow : Window
    {
        public ChangeLogWindow()
        {
            InitializeComponent();
            LoadChangeLog();
        }

        private void LoadChangeLog()
        {
            txtContent.Inlines.Clear();

            AddHeading("HashDash — What's New", 20, true);
            AddText("A summary of improvements in each release\n\n", 11, Colors.Gray);

            // Version 2.2
            AddVersion("Version 2.2", "July 2026");
            AddText("Support for hashing and copying directly from MTP-connected devices\n\n", color: Colors.Gray);

            AddSubheading("What's New");
            AddBullet("Connect Device — a new button next to Browse Source Folder lets you use a phone, camera, or drone controller (e.g. a DJI RC2) connected over USB as the source, without needing to copy files off it manually first");
            AddBullet("MTP devices are read in a read-only session — HashDash never writes to, deletes from, or modifies the connected device");
            AddBullet("Files read from a device go through the exact same hash-before-copy, copy, hash-after-copy, compare workflow as local and network folders — the verification is not weakened for device sources");
            AddBullet("Live status and a Cancel option while HashDash scans a connected device, since large photo/video libraries can take a while to enumerate\n");

            AddSubheading("Notes");
            AddBullet("iPhones and iPads only expose their photo library (Camera Roll) this way, not the full device storage — this is a restriction of iOS itself, not HashDash");
            AddBullet("MTP devices can currently only be used as a source, not as a destination\n\n");

            // Version 2.1
            AddVersion("Version 2.1", "April 2026");
            AddText("Quality improvements and new options based on real-world use\n\n", color: Colors.Gray);

            AddSubheading("What's New");
            AddBullet("Secondary destination — copy to two locations in a single run. Both copies are verified and the results appear in the file list, the transfer report, and the history log");
            AddBullet("Show or hide columns — right-click any column header to choose which columns are visible. Your preferences are saved and restored the next time you open the program");
            AddBullet("Search in History — you can now type in the History window to filter transfers by examiner, case reference, exhibit, or status");
            AddBullet("Automation startup delay — an optional countdown (default 2 minutes) before automation begins, giving you time to step away before processing starts");
            AddBullet("Settings are now saved next to the program file, making HashDash fully portable with no dependency on any specific computer or user profile\n");

            AddSubheading("Improvements");
            AddBullet("MD5 and SHA-256 checkboxes moved closer together for a tidier layout");
            AddBullet("Secondary destination section appears dimmed until a path is selected, reducing visual clutter");
            AddBullet("Several columns are now hidden by default (Path, MD5) to keep the file list clean — they can be shown at any time via the column menu");
            AddBullet("Secondary destination details now correctly appear in PDF reports and the history log\n\n");

            // Version 2.0
            AddVersion("Version 2.0", "March 2026");
            AddText("A full redesign — cleaner, faster, and easier to use\n\n", color: Colors.Gray);

            AddSubheading("What's New");
            AddBullet("Completely redesigned layout with a clear two-panel view: case details on the left, copy and verification controls on the right");
            AddBullet("Notifications — a bell icon shows alerts and completion messages. A red badge indicates unread items");
            AddBullet("Pop-up alerts when a transfer completes, fails, or is cancelled");
            AddBullet("Screenshot button — capture the current screen at any point");
            AddBullet("Duplicate file detection — identifies files that are identical based on their content");
            AddBullet("Automatic mode — HashDash watches a folder and starts automatically when a nominated trigger file appears");
            AddBullet("Full history log — every transfer is recorded in a searchable database, accessible from the Help menu");
            AddBullet("Drive labels displayed next to source and destination paths so you can confirm you have the right disk");
            AddBullet("PDF report generated automatically after every copy and verify operation, saved to a 'Data Transfer Logs' folder at the destination");
            AddBullet("Case fields are locked while a transfer is running to prevent accidental changes");
            AddBullet("Colour-coded status banner shows whether automation is waiting, running, or finished\n");

            AddSubheading("Improvements");
            AddBullet("PDF reports now use a consistent, readable layout regardless of how they are generated");
            AddBullet("Export file names are automatically based on your exhibit and case reference");
            AddBullet("Recent folder paths saved for quicker access");
            AddBullet("Improved responsiveness on large file sets");
            AddBullet("Dark and light theme follow the Windows system setting automatically\n\n");

            // Version 1.0
            AddVersion("Version 1.0", "2025 — Initial Release");
            AddText("The first version of HashDash\n\n", color: Colors.Gray);

            AddSubheading("Core Features");
            AddBullet("Calculate MD5 and SHA-256 values for files in a selected folder");
            AddBullet("Copy files to a destination and confirm the copies match the originals");
            AddBullet("Export results to PDF, CSV, or a plain text log");
            AddBullet("Enter case reference, exhibit reference, and examiner name before each transfer");
            AddBullet("Basic automation support");
            AddBullet("Progress bar and per-file status during processing\n\n");

            AddText("\n───────────────────────────────────────────────────\n\n", color: Colors.Gray);
            AddText("HashDash v2.2  |  Written by Adeel Khan", 11, Colors.Gray);
        }

        private void AddVersion(string version, string date)
        {
            txtContent.Inlines.Add(new LineBreak());
            var versionRun = new Run(version + "\n")
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xFE))
            };
            txtContent.Inlines.Add(versionRun);

            var dateRun = new Run(date + "\n")
            {
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Colors.Gray)
            };
            txtContent.Inlines.Add(dateRun);
        }

        private void AddHeading(string text, int fontSize = 16, bool isTitle = false)
        {
            var run = new Run(text + "\n")
            {
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold
            };
            txtContent.Inlines.Add(run);
            
            if (!isTitle)
            {
                txtContent.Inlines.Add(new LineBreak());
            }
        }

        private void AddSubheading(string text)
        {
            var run = new Run("\n" + text + "\n")
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            txtContent.Inlines.Add(run);
        }

        private void AddText(string text, int fontSize = 13, Color? color = null, bool fontWeight = false)
        {
            var run = new Run(text)
            {
                FontSize = fontSize
            };

            if (color.HasValue)
            {
                run.Foreground = new SolidColorBrush(color.Value);
            }

            if (fontWeight)
            {
                run.FontWeight = FontWeights.SemiBold;
            }

            txtContent.Inlines.Add(run);
        }

        private void AddBullet(string text)
        {
            var run = new Run("  • " + text + "\n")
            {
                FontSize = 13
            };
            txtContent.Inlines.Add(run);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}