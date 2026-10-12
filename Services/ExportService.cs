using HashDash.Helpers;
using HashDash.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HashDash.Services
{
    public class ExportService
    {
        private const string AppName = "HashDash";
        private const string AppVersion = "2.0";

        /// <summary>
        /// Generates CSV content from file entries
        /// </summary>
        public string GenerateCsvContent(
            IEnumerable<FileHashEntry> files,
            string computerName,
            string examinerName = "",
            string labCaseRef = "",
            string exhibitRef = "",
            bool md5Enabled = true,
            bool sha256Enabled = true)
        {
            var sb = new StringBuilder();
            string exportedAt = DateTime.Now.ToString("g");

            // Header metadata
            sb.AppendLine($"Program,{FileNameHelper.EscapeCsv($"{AppName} v{AppVersion}")}");
            sb.AppendLine($"Computer Name,{FileNameHelper.EscapeCsv(computerName)}");
            sb.AppendLine($"Exported,{FileNameHelper.EscapeCsv(exportedAt)}");
            sb.AppendLine($"Examiner Name,{FileNameHelper.EscapeCsv(examinerName)}");
            sb.AppendLine($"Lab Case Reference,{FileNameHelper.EscapeCsv(labCaseRef)}");
            sb.AppendLine($"Exhibit Reference,{FileNameHelper.EscapeCsv(exhibitRef)}");
            sb.AppendLine();

            // Column headers
            sb.AppendLine(
                "\"#\"," +
                "\"Filename\"," +
                "\"Size\"," +
                "\"Source Path\"," +
                "\"Destination Path\"," +
                "\"MD5 (Source)\"," +
                "\"SHA-256 (Source)\"," +
                "\"MD5 (Dest)\"," +
                "\"SHA-256 (Dest)\"," +
                "\"Algorithms Used\"," +
                "\"Transfer Date\"," +
                "\"File Date\"," +
                "\"Status\"," +
                "\"Verification\"," +
                "\"Error Detail\"," +
                "\"Photos Albums\"," +
                "\"Saved By App\"," +
                "\"Library Role\"");

            string algsUsed = (md5Enabled ? "MD5" : "") +
                              (md5Enabled && sha256Enabled ? ", " : "") +
                              (sha256Enabled ? "SHA-256" : "");

            // Data rows
            foreach (var f in files)
            {
                sb.AppendLine(
                    $"\"{FileNameHelper.EscapeCsv(f.Id.ToString())}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.FileName)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.SizeDisplay)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.FullPath)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.DestPath)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.Md5)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.Sha256)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.DestMd5)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.DestSha256)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(algsUsed)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.TransferDate)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.FileDate)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.CopyStatus)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.VerificationResult)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.ErrorDetail)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.PhotoAlbumsDisplay)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.SavedByApp)}\"," +
                    $"\"{FileNameHelper.EscapeCsv(f.LibraryRole)}\"");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Saves CSV content to file
        /// </summary>
        public void SaveCsvToFile(string csvContent, string filePath)
        {
            File.WriteAllText(filePath, csvContent, Encoding.UTF8);
        }

        /// <summary>
        /// Writes files to one or more sequential CSVs (e.g. export_1.csv, export_2.csv).
        /// Returns the number of files written.
        /// </summary>
        public int SaveCsvChunked(
            string basePath,
            IEnumerable<FileHashEntry> files,
            string computerName,
            string examinerName,
            string labCaseRef,
            string exhibitRef,
            bool md5Enabled,
            bool sha256Enabled,
            int rowsPerFile)
        {
            string algsUsed = (md5Enabled ? "MD5" : "") +
                              (md5Enabled && sha256Enabled ? ", " : "") +
                              (sha256Enabled ? "SHA-256" : "");

            string dir      = Path.GetDirectoryName(basePath) ?? "";
            string nameOnly = Path.GetFileNameWithoutExtension(basePath);
            string ext      = Path.GetExtension(basePath);

            var allFiles = files.ToList();
            int totalChunks = Math.Max(1, (int)Math.Ceiling(allFiles.Count / (double)rowsPerFile));

            for (int chunk = 0; chunk < totalChunks; chunk++)
            {
                var slice = allFiles.Skip(chunk * rowsPerFile).Take(rowsPerFile);

                string chunkPath = totalChunks == 1
                    ? basePath
                    : Path.Combine(dir, $"{nameOnly}_{chunk + 1}{ext}");

                var sb = new StringBuilder();
                string exportedAt = DateTime.Now.ToString("g");

                sb.AppendLine($"Program,{FileNameHelper.EscapeCsv($"{AppName} v{AppVersion}")}");
                sb.AppendLine($"Computer Name,{FileNameHelper.EscapeCsv(computerName)}");
                sb.AppendLine($"Exported,{FileNameHelper.EscapeCsv(exportedAt)}");
                sb.AppendLine($"Examiner Name,{FileNameHelper.EscapeCsv(examinerName)}");
                sb.AppendLine($"Lab Case Reference,{FileNameHelper.EscapeCsv(labCaseRef)}");
                sb.AppendLine($"Exhibit Reference,{FileNameHelper.EscapeCsv(exhibitRef)}");
                if (totalChunks > 1)
                    sb.AppendLine($"Part,{chunk + 1} of {totalChunks}");
                sb.AppendLine();

                sb.AppendLine(
                    "\"#\"," +
                    "\"Filename\"," +
                    "\"Size\"," +
                    "\"Source Path\"," +
                    "\"Destination Path\"," +
                    "\"MD5 (Source)\"," +
                    "\"SHA-256 (Source)\"," +
                    "\"MD5 (Dest)\"," +
                    "\"SHA-256 (Dest)\"," +
                    "\"Algorithms Used\"," +
                    "\"Transfer Date\"," +
                    "\"File Date\"," +
                    "\"Status\"," +
                    "\"Verification\"," +
                    "\"Error Detail\"," +
                    "\"Photos Albums\"," +
                    "\"Saved By App\"," +
                    "\"Library Role\"");

                foreach (var f in slice)
                {
                    sb.AppendLine(
                        $"\"{FileNameHelper.EscapeCsv(f.Id.ToString())}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.FileName)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.SizeDisplay)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.FullPath)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.DestPath)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.Md5)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.Sha256)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.DestMd5)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.DestSha256)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(algsUsed)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.TransferDate)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.FileDate)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.CopyStatus)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.VerificationResult)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.ErrorDetail)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.PhotoAlbumsDisplay)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.SavedByApp)}\"," +
                        $"\"{FileNameHelper.EscapeCsv(f.LibraryRole)}\"");
                }

                File.WriteAllText(chunkPath, sb.ToString(), Encoding.UTF8);
            }

            return totalChunks;
        }

        /// <summary>
        /// Builds a FlowDocument for PDF export (compact format)
        /// </summary>
        public FlowDocument BuildPdfFlowDocument(
            IEnumerable<FileHashEntry> files,
            string computerName,
            string examinerName = "",
            string labCaseRef = "",
            string exhibitRef = "",
            bool md5Enabled = true,
            bool sha256Enabled = true)
        {
            string exportedAt = DateTime.Now.ToString("g");

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10
            };

            // Title
            doc.Blocks.Add(new Paragraph(new Run($"{AppName} — Hash Transfer Report"))
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                TextAlignment = TextAlignment.Center
            });

            // Metadata (Compact)
            var metaPara = new Paragraph
            {
                FontSize = 9,
                Margin = new Thickness(0, 0, 0, 10)
            };
            metaPara.Inlines.Add(new Run("Program: ") { FontWeight = FontWeights.SemiBold });
            metaPara.Inlines.Add(new Run($"{AppName} v{AppVersion}  │  "));
            metaPara.Inlines.Add(new Run("Exported: ") { FontWeight = FontWeights.SemiBold });
            metaPara.Inlines.Add(new Run($"{exportedAt}\n"));
            metaPara.Inlines.Add(new Run("Computer: ") { FontWeight = FontWeights.SemiBold });
            metaPara.Inlines.Add(new Run($"{computerName}  │  "));
            metaPara.Inlines.Add(new Run("Examiner: ") { FontWeight = FontWeights.SemiBold });
            metaPara.Inlines.Add(new Run($"{(string.IsNullOrWhiteSpace(examinerName) ? "(not set)" : examinerName)}\n"));
            metaPara.Inlines.Add(new Run("Lab Case: ") { FontWeight = FontWeights.SemiBold });
            metaPara.Inlines.Add(new Run($"{(string.IsNullOrWhiteSpace(labCaseRef) ? "(not set)" : labCaseRef)}  │  "));
            metaPara.Inlines.Add(new Run("Exhibit: ") { FontWeight = FontWeights.SemiBold });
            metaPara.Inlines.Add(new Run($"{(string.IsNullOrWhiteSpace(exhibitRef) ? "(not set)" : exhibitRef)}"));
            doc.Blocks.Add(metaPara);

            doc.Blocks.Add(new Paragraph
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Files (Grouped compact format)
            foreach (var f in files.OrderBy(x => x.Id))
            {
                var filePara = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    FontSize = 9
                };

                // Line 1: ID and Filename (bold)
                filePara.Inlines.Add(new Run($"[{f.Id}] {f.FileName}")
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 10
                });
                filePara.Inlines.Add(new LineBreak());

                // Line 2: Paths
                filePara.Inlines.Add(new Run("Source: ") { FontWeight = FontWeights.SemiBold });
                filePara.Inlines.Add(new Run(f.FullPath));
                filePara.Inlines.Add(new LineBreak());
                filePara.Inlines.Add(new Run("Destination: ") { FontWeight = FontWeights.SemiBold });
                filePara.Inlines.Add(new Run(f.DestPath ?? "(none)"));
                filePara.Inlines.Add(new LineBreak());

                // Line 3: Metadata
                filePara.Inlines.Add(new Run("Size: ") { FontWeight = FontWeights.SemiBold });
                filePara.Inlines.Add(new Run($"{f.SizeDisplay}  │  "));
                filePara.Inlines.Add(new Run("File Date: ") { FontWeight = FontWeights.SemiBold });
                filePara.Inlines.Add(new Run($"{f.FileDate}  │  "));
                filePara.Inlines.Add(new Run("Transfer: ") { FontWeight = FontWeights.SemiBold });
                filePara.Inlines.Add(new Run(f.TransferDate ?? "N/A"));
                filePara.Inlines.Add(new LineBreak());

                // Line 4: Status
                filePara.Inlines.Add(new Run("Status: ") { FontWeight = FontWeights.SemiBold });
                var statusRun = new Run(f.CopyStatus ?? "N/A")
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(GetResultColor(f.VerificationResult))
                };
                filePara.Inlines.Add(statusRun);
                filePara.Inlines.Add(new Run("  │  "));
                filePara.Inlines.Add(new Run("Result: ") { FontWeight = FontWeights.SemiBold });
                var resultRun = new Run(f.VerificationResult ?? "N/A")
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(GetResultColor(f.VerificationResult))
                };
                filePara.Inlines.Add(resultRun);
                filePara.Inlines.Add(new LineBreak());

                // Hash values (if enabled)
                if (md5Enabled)
                {
                    filePara.Inlines.Add(new Run("MD5 Source: ") { FontWeight = FontWeights.SemiBold });
                    filePara.Inlines.Add(new Run(f.Md5 ?? "") { FontFamily = new FontFamily("Consolas"), FontSize = 8 });
                    filePara.Inlines.Add(new LineBreak());
                    filePara.Inlines.Add(new Run("MD5 Dest:   ") { FontWeight = FontWeights.SemiBold });
                    filePara.Inlines.Add(new Run(f.DestMd5 ?? "") { FontFamily = new FontFamily("Consolas"), FontSize = 8 });
                    filePara.Inlines.Add(new LineBreak());
                }

                if (sha256Enabled)
                {
                    filePara.Inlines.Add(new Run("SHA-256 Source: ") { FontWeight = FontWeights.SemiBold });
                    filePara.Inlines.Add(new Run(f.Sha256 ?? "") { FontFamily = new FontFamily("Consolas"), FontSize = 7 });
                    filePara.Inlines.Add(new LineBreak());
                    filePara.Inlines.Add(new Run("SHA-256 Dest:   ") { FontWeight = FontWeights.SemiBold });
                    filePara.Inlines.Add(new Run(f.DestSha256 ?? "") { FontFamily = new FontFamily("Consolas"), FontSize = 7 });
                    filePara.Inlines.Add(new LineBreak());
                }

                if (!string.IsNullOrWhiteSpace(f.ErrorDetail))
                {
                    filePara.Inlines.Add(new Run("Error: ") { FontWeight = FontWeights.SemiBold, Foreground = Brushes.Red });
                    filePara.Inlines.Add(new Run(f.ErrorDetail) { Foreground = Brushes.Red });
                    filePara.Inlines.Add(new LineBreak());
                }

                doc.Blocks.Add(filePara);

                // Separator
                doc.Blocks.Add(new Paragraph
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            return doc;
        }

        private static Color GetResultColor(string? result) => result switch
        {
            var r when r != null && r.Contains("Verified") => Color.FromRgb(0x10, 0x7C, 0x10),
            "MISMATCH" or "Failed" => Color.FromRgb(0xC4, 0x1A, 0x1A),
            "Cancelled" => Color.FromRgb(0xD8, 0x7C, 0x00),
            _ => Colors.Black
        };
    }
}