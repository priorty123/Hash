using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HashDash
{
    public partial class UserManualWindow : Window
    {
        public UserManualWindow()
        {
            InitializeComponent();
            LoadManualContent();
        }

        private void LoadManualContent()
        {
            txtContent.Inlines.Clear();

            AddHeading("HashDash User Manual", 20, true);
            AddText("Version 2.2\n\n", 11, Colors.Gray);

            // Introduction
            AddHeading("What is HashDash?");
            AddText(
                "HashDash is a utility for copying data from one place to another with complete confidence that nothing " +
                "has changed in transit. Every file is checked before and after the transfer, and the results are " +
                "recorded automatically. This gives you a clear, reliable record of every transfer — suitable for " +
                "professional environments where accuracy and accountability matter.\n\n"
            );

            AddText("In plain terms, HashDash:\n", fontWeight: true);
            AddText(
                "✓ Copies files from a source folder to one or two destinations\n" +
                "✓ Confirms every file arrived intact\n" +
                "✓ Produces a PDF report and saves a history of every transfer\n" +
                "✓ Can run automatically when a nominated file appears in a folder\n\n"
            );

            // Interface Overview
            AddHeading("The Main Screen");

            AddSubheading("Left Panel — Case Details");
            AddText(
                "Fill these in before starting a transfer. They appear on your PDF report and in the transfer history.\n\n" +
                "• Examiner Name — the person running the transfer\n" +
                "• Lab Case Reference — your case or job number\n" +
                "• Exhibit Reference — the reference for this particular item\n\n"
            );
            AddText("All three fields must be filled in when using Automatic mode.\n\n", color: Colors.Gray);

            AddSubheading("Right Panel — Copy & Verify");
            AddText(
                "• Source — the folder you want to copy from. Click the folder icon to browse. Click the phone icon next to it to connect to an MTP device (phone, camera, or drone controller) instead — see \"How To: Copy Files From a Connected Device\" below.\n" +
                "• Destination — where the files should be copied to. Leave blank to calculate checksums only (no copying).\n" +
                "• Secondary Destination (optional) — a second copy location. HashDash will copy to both places and verify both in one run.\n" +
                "• SHA-256 / MD5 — tick which checks you want. SHA-256 is recommended; MD5 can be added for compatibility with other tools.\n" +
                "• Run — the blue button at the bottom right. Click it to start.\n\n"
            );

            AddSubheading("Top Bar");
            AddText(
                "• Automation — switches the automatic trigger on or off\n" +
                "• 🔔 Bell — shows recent alerts and completion messages. A red badge means there are unread items.\n" +
                "• 📷 Camera — takes a screenshot of the current window\n" +
                "• 🔍 Duplicates — checks whether any files in the list are identical to each other\n" +
                "• ⚙ Settings — configure automation, database location, and other options\n" +
                "• ? Help — access this manual, the change log, and the About screen\n\n"
            );

            // Tasks
            AddHeading("How To: Check Files Without Copying");
            AddText(
                "Use this when you want to record checksums of a folder without moving anything.\n\n"
            );
            AddText("Steps:\n", fontWeight: true);
            AddText(
                "1. Enter your case details on the left\n" +
                "2. Select the source folder\n" +
                "3. Leave Destination blank\n" +
                "4. Tick SHA-256 (and MD5 if needed)\n" +
                "5. Click Run\n\n"
            );
            AddText("When finished: ", fontWeight: true);
            AddText("each file shows a green \"Hash Complete\" status and its checksum values in the table.\n\n");

            AddHeading("How To: Copy Files and Verify the Transfer");
            AddText(
                "Use this for a standard data transfer. HashDash copies the files and then checks every one to confirm the copy is identical to the original.\n\n"
            );
            AddText("Steps:\n", fontWeight: true);
            AddText(
                "1. Enter your case details\n" +
                "2. Select the source folder\n" +
                "3. Select the destination folder\n" +
                "4. Tick SHA-256 (and MD5 if needed)\n" +
                "5. Click Run\n\n"
            );
            AddText("When finished: ", fontWeight: true);
            AddText(
                "each file shows \"Verified\" in green if the copy matches, or \"Hash Mismatch\" in red if it does not. " +
                "A PDF report is saved automatically to a 'Data Transfer Logs' folder inside the destination.\n\n"
            );

            AddHeading("How To: Copy Files From a Connected Device");
            AddText(
                "Use this for phones, cameras, drone controllers, or any device connected over USB that shows up in \"This PC\" but isn't a real drive letter or " +
                "network path — for example, a DJI RC2 controller or an Android phone. HashDash reads directly from the device in a read-only session; " +
                "it never writes to, deletes from, or changes anything on it.\n\n"
            );
            AddText("Steps:\n", fontWeight: true);
            AddText(
                "1. Connect the device by USB and, if prompted on the device itself, allow file access / choose \"File Transfer\" mode\n" +
                "2. Enter your case details\n" +
                "3. Click the phone icon next to Source (instead of the folder icon)\n" +
                "4. Select your device from the list and click Load Files — this may take a while for a large photo or video library, and shows live progress with a Cancel option\n" +
                "5. Select the destination folder\n" +
                "6. Tick SHA-256 (and MD5 if needed)\n" +
                "7. Click Run\n\n"
            );
            AddText("When finished: ", fontWeight: true);
            AddText(
                "each file is verified exactly as it would be for a local or network source. The Source Path field and the session ID both record the device name.\n\n"
            );
            AddText("Note: ", fontWeight: true);
            AddText(
                "iPhones and iPads only expose their Camera Roll (photos and videos) this way — this is a restriction of iOS itself, not HashDash. " +
                "Android devices such as the DJI RC2 expose their full internal storage.\n\n"
            );

            AddHeading("How To: Copy to Two Locations at Once");
            AddText(
                "Use this when you need to send data to a primary location and keep a second copy elsewhere — for example, distributing to an analyst and archiving at the same time.\n\n"
            );
            AddText("Steps:\n", fontWeight: true);
            AddText(
                "1. Follow the same steps as a standard copy\n" +
                "2. Also click the folder icon next to Secondary Destination and choose the second location\n" +
                "3. Click Run\n\n"
            );
            AddText("HashDash copies to the primary destination first, then the secondary, and verifies both. " +
                "The status for each file reflects the result of both copies.\n\n");

            AddHeading("How To: Use Automatic Mode");
            AddText(
                "Automatic mode is useful when you want HashDash to start a transfer on its own — for example, as soon as an acquisition tool finishes and drops a completion file into the folder.\n\n"
            );
            AddText("Setting it up (first time only):\n", fontWeight: true);
            AddText(
                "1. Go to Settings → Automation tab\n" +
                "2. Enter the trigger filename (e.g. report.xml) — this is the file HashDash watches for\n" +
                "3. Adjust the startup delay if needed (default is 2 minutes)\n" +
                "4. Click Save\n\n"
            );
            AddText("Starting automatic mode:\n", fontWeight: true);
            AddText(
                "1. Fill in all case details\n" +
                "2. Select source and destination folders\n" +
                "3. Click the Automation button — it turns on and a yellow banner appears\n" +
                "4. When the trigger file appears in the source folder, HashDash waits for the delay then starts automatically\n" +
                "5. On completion it turns off and is ready for the next case\n\n"
            );

            // Results
            AddHeading("Understanding the Status Column");

            AddText("✓  Verified  (green)\n", color: Colors.Green, fontWeight: true);
            AddText("   The copy matches the original exactly.\n\n");

            AddText("✓  Verified (Primary + Secondary)  (green)\n", color: Colors.Green, fontWeight: true);
            AddText("   Both copies match the original.\n\n");

            AddText("✓  Hash Complete  (green)\n", color: Colors.Green, fontWeight: true);
            AddText("   Checksum recorded successfully (no copy was made).\n\n");

            AddText("✗  Hash Mismatch  (red)\n", color: Colors.Red, fontWeight: true);
            AddText("   The copy does not match the original. Do not use this file — investigate before proceeding.\n\n");

            AddText("✗  Failed  (red)\n", color: Colors.Red, fontWeight: true);
            AddText("   An error prevented the file from being processed. See the error detail column.\n\n");

            AddText("⚠  Cancelled  (orange)\n", color: Colors.Orange, fontWeight: true);
            AddText("   The transfer was stopped by the user before completing.\n\n");

            // Columns
            AddHeading("Showing and Hiding Columns");
            AddText(
                "Right-click any column header to open a menu listing all available columns. Tick or untick to show or hide each one. " +
                "Your choices are saved automatically and restored the next time you open HashDash.\n\n" +
                "By default, the Path, MD5, and secondary hash columns are hidden to keep the view uncluttered. " +
                "They can be shown at any time from this menu.\n\n"
            );

            // History
            AddHeading("Transfer History");
            AddText(
                "HashDash records every transfer in a local database. To view the history, click the Help (?) icon and select Logs, " +
                "or look for the History option in the menu.\n\n" +
                "In the History window you can:\n" +
                "• Filter by date range\n" +
                "• Search by examiner, case reference, exhibit, or status\n" +
                "• Click any entry to see the individual file results for that transfer\n\n"
            );

            // Exporting
            AddHeading("Saving and Exporting Results");

            AddText("PDF Report (recommended):\n", fontWeight: true);
            AddText("Click the PDF icon in the toolbar above the file list. The report includes your case details, a summary, and the result for every file.\n\n");

            AddText("CSV Spreadsheet:\n", fontWeight: true);
            AddText("Click the CSV icon to export the file list to a format that opens in Excel or any spreadsheet application.\n\n");

            AddText("Detailed Log:\n", fontWeight: true);
            AddText("Click the log icon for a timestamped plain-text record of every action taken during the transfer.\n\n");

            AddText("Automatic PDF:\n", fontWeight: true);
            AddText("When copying files, a PDF is generated automatically and saved to a 'Data Transfer Logs' folder inside the destination.\n\n");

            // Troubleshooting
            AddHeading("If Something Goes Wrong");

            AddText("\"Access Denied\" error:\n", fontWeight: true);
            AddText(
                "• Make sure no other program has the source files open\n" +
                "• Check that you have write permission on the destination folder\n" +
                "• For network locations, confirm your login credentials are valid\n\n"
            );

            AddText("Automatic mode not starting:\n", fontWeight: true);
            AddText(
                "• All case detail fields must be filled in\n" +
                "• A destination folder must be selected\n" +
                "• Confirm the trigger filename in Settings matches exactly what the source tool creates\n" +
                "• The Automation button must show as ON (yellow banner visible)\n\n"
            );

            AddText("Hash Mismatch on a file:\n", fontWeight: true);
            AddText(
                "• Stop and note which file is affected before doing anything else\n" +
                "• Try copying that file again to rule out a temporary issue\n" +
                "• If the mismatch persists, the source or destination media may have a problem — check both\n" +
                "• Record the event using the export options before closing HashDash\n\n"
            );

            // Tips
            AddHeading("Tips");
            AddText(
                "✓ SHA-256 is the preferred option — use it unless you have a specific reason to use MD5\n" +
                "✓ Fill in all case details before clicking Run — they cannot be changed mid-transfer\n" +
                "✓ Keep the PDF report alongside the data you transferred\n" +
                "✓ Do not interrupt a transfer once it has started\n" +
                "✓ Check available disk space on the destination before copying large datasets\n\n"
            );

            // File locations
            AddHeading("File Locations");
            AddText(
                "All HashDash files are stored in the same folder as the program:\n\n" +
                "• HashDash_settings.json — your preferences and column layout\n" +
                "• ProcessLog.db — the transfer history database\n" +
                "• PDF Reports (automatic) — saved to [Destination]\\Data Transfer Logs\\\n\n" +
                "Because everything sits next to the program file, HashDash is fully portable — " +
                "copy it to any drive and your settings and history travel with it.\n\n"
            );

            AddText("\n───────────────────────────────────────────────────\n\n", color: Colors.Gray);
            AddText("For version information click Help → About.\n\n", 11, Colors.Gray);
            AddText("HashDash v2.2  |  Written by Adeel Khan", 11, Colors.Gray);
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
            var run = new Run(text + "\n")
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}