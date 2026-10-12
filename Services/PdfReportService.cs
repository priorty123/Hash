using HashDash.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HashDash.Services
{
    public class PdfReportService
    {
        public PdfReportService()
        {
            // Set QuestPDF license (Community license is free for open-source/personal use)
            QuestPDF.Settings.License = LicenseType.Community;
        }

public void GenerateReport(
    string outputPath,
    string examinerName,
    string labCaseRef,
    string exhibitRef,
    string computerName,
    string sourcePath,
    string sourceDriveLabel,
    string destinationPath,
    string destinationDriveLabel,
    DateTime startTime,
    DateTime endTime,
    List<FileHashEntry> files,
    bool md5Enabled,
    bool sha256Enabled,
    string secondaryDestinationPath = "",
    string secondaryDestinationDriveLabel = "")
{
    try
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                // HEADER
                page.Header().Column(column =>
                {
                    column.Item().PaddingBottom(10).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("HashDash - Data Transfer Report")
                                .FontSize(18)
                                .Bold();
                            col.Item().Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
                                .FontSize(9);
                        });
                    });

                    // Case Information
                    column.Item().PaddingTop(10).PaddingBottom(5)
                        .BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

                    column.Item().PaddingTop(8).PaddingBottom(8).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text($"Examiner: {examinerName}").FontSize(10);
                            col.Item().Text($"Lab Case Ref: {labCaseRef}").FontSize(10);
                            col.Item().Text($"Exhibit Ref: {exhibitRef}").FontSize(10);
                        });
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text($"Computer: {computerName}").FontSize(10);
                            col.Item().Text($"Start: {startTime:dd/MM/yyyy HH:mm:ss}").FontSize(10);
                            col.Item().Text($"End: {endTime:dd/MM/yyyy HH:mm:ss}").FontSize(10);
                        });
                    });

                    // Source and Destination
                    column.Item().PaddingTop(8).PaddingBottom(8).Column(col =>
                    {
                        col.Item().Text(text =>
                        {
                            text.Span("Source: ").Bold();
                            text.Span($"{sourcePath}").FontSize(9);
                            if (!string.IsNullOrEmpty(sourceDriveLabel))
                                text.Span($" ({sourceDriveLabel})").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        col.Item().PaddingTop(4).Text(text =>
                        {
                            text.Span("Destination: ").Bold();
                            text.Span($"{destinationPath}").FontSize(9);
                            if (!string.IsNullOrEmpty(destinationDriveLabel))
                                text.Span($" ({destinationDriveLabel})").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        if (!string.IsNullOrWhiteSpace(secondaryDestinationPath))
                        {
                            col.Item().PaddingTop(4).Text(text =>
                            {
                                text.Span("Secondary Destination: ").Bold();
                                text.Span(secondaryDestinationPath).FontSize(9);
                                if (!string.IsNullOrEmpty(secondaryDestinationDriveLabel))
                                    text.Span($" ({secondaryDestinationDriveLabel})").FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        }
                    });

                    column.Item().PaddingTop(5).PaddingBottom(10)
                        .BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                });

                // CONTENT - FILE ENTRIES IN PARAGRAPH STYLE
                page.Content().Column(column =>
                {
                    column.Item().PaddingTop(10).Text("File Transfer Details")
                        .FontSize(14)
                        .Bold();

                    column.Item().PaddingTop(5).Text($"Total Files: {files.Count} | MD5: {(md5Enabled ? "Enabled" : "Disabled")} | SHA-256: {(sha256Enabled ? "Enabled" : "Disabled")}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);

                    column.Item().PaddingTop(10);

                    // ✅ PARAGRAPH-STYLE FILE ENTRIES
                    foreach (var file in files)
                    {
                        column.Item().PaddingBottom(12).Column(fileColumn =>
                        {
                            // File Header with Status Color
                            fileColumn.Item().PaddingBottom(4).Row(headerRow =>
                            {
                                headerRow.RelativeItem().Text(text =>
                                {
                                    text.Span("File: ").Bold();
                                    text.Span(file.FileName).FontSize(10).Bold();
                                });

                                headerRow.AutoItem().AlignRight().Text(text =>
                                {
                                    string status = file.CopyStatus ?? "Unknown";
                                    
                                    text.Span("Status: ").FontSize(9);
                                    
                                    // ✅ FIXED: Apply color based on status
                                    var statusSpan = text.Span(status).FontSize(9).Bold();
                                    
                                    if (status == "Verified" || status == "Verified (Existing)" || status == "Hash Complete")
                                        statusSpan.FontColor(Colors.Green.Darken2);
                                    else if (status == "Failed" || status == "Hash Mismatch" || status == "MISMATCH")
                                        statusSpan.FontColor(Colors.Red.Medium);
                                    else if (status == "Cancelled")
                                        statusSpan.FontColor(Colors.Orange.Medium);
                                    else
                                        statusSpan.FontColor(Colors.Grey.Darken1);
                                });
                            });

                            // File Details in Paragraph Style
                            fileColumn.Item().PaddingLeft(10).Column(detailColumn =>
                            {
                                // SHA-256 Source
                                if (sha256Enabled && !string.IsNullOrEmpty(file.Sha256))
                                {
                                    detailColumn.Item().PaddingBottom(3).Text(text =>
                                    {
                                        text.Span("SHA-256 (Source): ").FontSize(9).Bold();
                                        text.Span(file.Sha256).FontSize(8).FontFamily("Courier New");
                                    });
                                }

                                // SHA-256 Destination
                                if (sha256Enabled && !string.IsNullOrEmpty(file.DestSha256))
                                {
                                    detailColumn.Item().PaddingBottom(3).Text(text =>
                                    {
                                        text.Span("SHA-256 (Dest):   ").FontSize(9).Bold();
                                        text.Span(file.DestSha256).FontSize(8).FontFamily("Courier New");
                                    });
                                }

                                // MD5 Source
                                if (md5Enabled && !string.IsNullOrEmpty(file.Md5))
                                {
                                    detailColumn.Item().PaddingBottom(3).Text(text =>
                                    {
                                        text.Span("MD5 (Source): ").FontSize(9).Bold();
                                        text.Span(file.Md5).FontSize(8).FontFamily("Courier New");
                                    });
                                }

                                // MD5 Destination
                                if (md5Enabled && !string.IsNullOrEmpty(file.DestMd5))
                                {
                                    detailColumn.Item().PaddingBottom(3).Text(text =>
                                    {
                                        text.Span("MD5 (Dest):   ").FontSize(9).Bold();
                                        text.Span(file.DestMd5).FontSize(8).FontFamily("Courier New");
                                    });
                                }

                                // Secondary destination hashes
                                if (!string.IsNullOrWhiteSpace(secondaryDestinationPath))
                                {
                                    if (sha256Enabled && !string.IsNullOrEmpty(file.SecondaryDestSha256))
                                    {
                                        detailColumn.Item().PaddingBottom(3).Text(text =>
                                        {
                                            text.Span("SHA-256 (Secondary): ").FontSize(9).Bold();
                                            text.Span(file.SecondaryDestSha256).FontSize(8).FontFamily("Courier New");
                                        });
                                    }

                                    if (md5Enabled && !string.IsNullOrEmpty(file.SecondaryDestMd5))
                                    {
                                        detailColumn.Item().PaddingBottom(3).Text(text =>
                                        {
                                            text.Span("MD5 (Secondary): ").FontSize(9).Bold();
                                            text.Span(file.SecondaryDestMd5).FontSize(8).FontFamily("Courier New");
                                        });
                                    }
                                }

                                // File Metadata
                                detailColumn.Item().PaddingTop(3).PaddingBottom(2).Text(text =>
                                {
                                    text.Span("Size: ").FontSize(9).Bold();
                                    // ✅ FIXED: Use SizeDisplay property or format bytes
                                    text.Span(FormatFileSize(file.FileSizeBytes)).FontSize(9);
                                    text.Span("  |  File Date: ").FontSize(9).Bold();
                                    text.Span(file.FileDate ?? "N/A").FontSize(9);
                                    
                                    if (!string.IsNullOrEmpty(file.TransferDate))
                                    {
                                        text.Span("  |  Transfer Date: ").FontSize(9).Bold();
                                        text.Span(file.TransferDate).FontSize(9);
                                    }
                                });

                                // Verification Result (if applicable)
                                if (!string.IsNullOrEmpty(file.VerificationResult) && 
                                    file.VerificationResult != "N/A")
                                {
                                    detailColumn.Item().PaddingTop(2).Text(text =>
                                    {
                                        text.Span("Verification: ").FontSize(9).Bold();
                                        
                                        // ✅ FIXED: Color-coded verification result
                                        var verifySpan = text.Span(file.VerificationResult).FontSize(9).Bold();
                                        
                                        if (file.VerificationResult.Contains("MISMATCH") || file.VerificationResult == "Failed")
                                            verifySpan.FontColor(Colors.Red.Medium);
                                        else
                                            verifySpan.FontColor(Colors.Green.Darken2);
                                    });
                                }

                                // Error Details (if any)
                                if (!string.IsNullOrEmpty(file.ErrorDetail))
                                {
                                    detailColumn.Item().PaddingTop(3).Text(text =>
                                    {
                                        text.Span("Error: ").FontSize(9).Bold().FontColor(Colors.Red.Medium);
                                        text.Span(file.ErrorDetail).FontSize(8).FontColor(Colors.Red.Darken1);
                                    });
                                }
                            });

                            // Separator line between files
                            fileColumn.Item().PaddingTop(8)
                                .BorderBottom(1)
                                .BorderColor(Colors.Grey.Lighten3);
                        });
                    }
                });

                // FOOTER
// FOOTER
page.Footer()
    .AlignCenter()
    .DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1))
    .Text(text =>
    {
        text.Span("Page ");
        text.CurrentPageNumber();
        text.Span(" of ");
        text.TotalPages();
        text.Span($"  |  Generated by HashDash v2.0  |  {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
    });
            });
        }).GeneratePdf(outputPath);
    }
    catch (Exception ex)
    {
        throw new Exception($"Error generating PDF report: {ex.Message}", ex);
    }
}

// ✅ ADD THIS HELPER METHOD AT THE END OF THE CLASS
private static string FormatFileSize(long bytes)
{
    string[] sizes = { "B", "KB", "MB", "GB", "TB" };
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len = len / 1024;
    }
    return $"{len:0.##} {sizes[order]}";
}

private static string FormatBytes(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
    if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
    return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
}



        /// <summary>
/// Generate PDF for manual export (print-style formatting)
/// </summary>
public void GenerateManualExportPdf(
    string outputPath,
    string examinerName,
    string labCaseRef,
    string exhibitRef,
    string computerName,
    string sourcePath,
    string sourceDriveLabel,
    string destinationPath,
    string destinationDriveLabel,
    DateTime startTime,
    DateTime endTime,
    List<FileHashEntry> files,
    bool md5Enabled,
    bool sha256Enabled,
    string secondaryDestinationPath = "",
    string secondaryDestinationDriveLabel = "",
    string sessionId = "")
{
    var duration = endTime - startTime;
    var totalSize = files.Sum(f => f.FileSizeBytes);

    Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

            page.Header().Element(ComposeManualHeader);
            page.Content().Element(content => ComposeManualContent(content, files));
            page.Footer().Element(ComposeFooter);
        });

        void ComposeManualHeader(IContainer container)
        {
            container.Column(column =>
            {
                // Title
                column.Item().AlignCenter().Text("DATA TRANSFER VERIFICATION REPORT")
                    .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                column.Item().PaddingVertical(10);

                // Metadata Grid (2 columns for compact layout)
                column.Item().Row(row =>
                {
                    // Left column
                    row.RelativeItem().Column(leftCol =>
                    {
                        leftCol.Item().Text(txt =>
                        {
                            txt.Span("Case Reference: ").FontSize(11).Bold();
                            txt.Span(labCaseRef).FontSize(11);
                        });
                        leftCol.Item().PaddingVertical(2);

                        leftCol.Item().Text(txt =>
                        {
                            txt.Span("Exhibit Reference: ").FontSize(11).Bold();
                            txt.Span(exhibitRef).FontSize(11);
                        });
                        leftCol.Item().PaddingVertical(2);

                        leftCol.Item().Text(txt =>
                        {
                            txt.Span("Examiner: ").FontSize(11).Bold();
                            txt.Span(examinerName).FontSize(11);
                        });
                    });

                    // Right column
                    row.RelativeItem().Column(rightCol =>
                    {
                        rightCol.Item().Text(txt =>
                        {
                            txt.Span("Computer: ").FontSize(11).Bold();
                            txt.Span(computerName).FontSize(11);
                        });
                        rightCol.Item().PaddingVertical(2);

                        rightCol.Item().Text(txt =>
                        {
                            txt.Span("Date: ").FontSize(11).Bold();
                            txt.Span(startTime.ToString("yyyy-MM-dd HH:mm:ss")).FontSize(11);
                        });
                        rightCol.Item().PaddingVertical(2);

                        rightCol.Item().Text(txt =>
                        {
                            txt.Span("Duration: ").FontSize(11).Bold();
                            txt.Span($"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s").FontSize(11);
                        });
                    });
                });

                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    column.Item().PaddingTop(6).Text(txt =>
                    {
                        txt.Span("Session ID: ").FontSize(10).Bold();
                        txt.Span(sessionId).FontSize(10).FontFamily("Courier New");
                    });
                }

                column.Item().PaddingVertical(5);
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
                column.Item().PaddingVertical(5);

                // Source/Destination
                column.Item().Text(txt =>
                {
                    txt.Span("Source: ").FontSize(11).Bold();
                    txt.Span($"{sourcePath} {sourceDriveLabel}").FontSize(10);
                });

                if (!string.IsNullOrWhiteSpace(destinationPath))
                {
                    column.Item().PaddingVertical(2);
                    column.Item().Text(txt =>
                    {
                        txt.Span("Destination: ").FontSize(11).Bold();
                        txt.Span($"{destinationPath} {destinationDriveLabel}").FontSize(10);
                    });
                }

                if (!string.IsNullOrWhiteSpace(secondaryDestinationPath))
                {
                    column.Item().PaddingVertical(2);
                    column.Item().Text(txt =>
                    {
                        txt.Span("Secondary Destination: ").FontSize(11).Bold();
                        txt.Span($"{secondaryDestinationPath} {secondaryDestinationDriveLabel}").FontSize(10);
                    });
                }

                column.Item().PaddingVertical(5);
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
            });
        }

        void ComposeManualContent(IContainer container, List<FileHashEntry> fileList)
        {
            container.PaddingVertical(10).Column(column =>
            {
                // Summary
                column.Item().Background(Colors.Blue.Lighten4).Padding(10).Row(row =>
                {
                    row.RelativeItem().Text($"Total Files: {fileList.Count}").FontSize(11).Bold();
                    row.RelativeItem().Text($"Total Size: {FormatBytes(totalSize)}").FontSize(11).Bold();

                    int verified = fileList.Count(f => f.VerificationResult?.Contains("Verified") == true);
                    int mismatches = fileList.Count(f => f.VerificationResult?.Contains("MISMATCH") == true);

                    if (verified > 0)
                        row.RelativeItem().Text($"Verified: {verified}").FontSize(11).Bold().FontColor(Colors.Green.Darken2);

                    if (mismatches > 0)
                        row.RelativeItem().Text($"Mismatches: {mismatches}").FontSize(11).Bold().FontColor(Colors.Red.Darken2);
                });

                column.Item().PaddingVertical(10);

                // File List (Paragraph Style - NOT table)
                foreach (var file in fileList)
                {
                    bool isVerified = file.VerificationResult?.Contains("Verified") == true;
                    bool isMismatch = file.VerificationResult?.Contains("MISMATCH") == true;

                    column.Item().Background(Colors.Grey.Lighten4).Padding(8).Column(fileCol =>
                    {
                        // File header
                        fileCol.Item().Row(headerRow =>
                        {
                            headerRow.RelativeItem().Text(txt =>
                            {
                                txt.Span($"#{file.Id} ").FontSize(10).Bold();
                                txt.Span(file.FileName).FontSize(11).Bold();
                            });

                            headerRow.AutoItem().Text(file.SizeDisplay)
                                .FontSize(10).FontColor(Colors.Grey.Darken1);
                        });

// Full source path
if (!string.IsNullOrWhiteSpace(file.FullPath))
{
    fileCol.Item().Text(txt =>
    {
        txt.Span("Path: ").FontSize(9).Bold();
        txt.Span(file.FullPath).FontSize(8).FontColor(Colors.Grey.Darken2);
    });
}

fileCol.Item().PaddingVertical(2);

// Status with color
if (!string.IsNullOrWhiteSpace(file.VerificationResult))
                        {
                            var statusColor = isVerified ? Colors.Green.Darken2
                                            : isMismatch ? Colors.Red.Darken2
                                            : Colors.Black;

                            fileCol.Item().Text(txt =>
                            {
                                txt.Span("Status: ").FontSize(10).Bold();
                                txt.Span(file.VerificationResult).FontSize(10).FontColor(statusColor);
                            });
                            fileCol.Item().PaddingVertical(2);
                        }

                        // Source hashes
                        if (sha256Enabled && !string.IsNullOrWhiteSpace(file.Sha256))
                        {
                            fileCol.Item().Text(txt =>
                            {
                                txt.Span("SHA-256 (Source): ").FontSize(9).Bold();
                                txt.Span(file.Sha256).FontSize(8).FontFamily("Courier New");
                            });
                        }

                        if (md5Enabled && !string.IsNullOrWhiteSpace(file.Md5))
                        {
                            fileCol.Item().Text(txt =>
                            {
                                txt.Span("MD5 (Source): ").FontSize(9).Bold();
                                txt.Span(file.Md5).FontSize(8).FontFamily("Courier New");
                            });
                        }

                        // Destination hashes (if available)
                        if (!string.IsNullOrWhiteSpace(destinationPath))
                        {
                            if (sha256Enabled && !string.IsNullOrWhiteSpace(file.DestSha256))
                            {
                                fileCol.Item().Text(txt =>
                                {
                                    txt.Span("SHA-256 (Destination): ").FontSize(9).Bold();
                                    txt.Span(file.DestSha256).FontSize(8).FontFamily("Courier New");
                                });
                            }

                            if (md5Enabled && !string.IsNullOrWhiteSpace(file.DestMd5))
                            {
                                fileCol.Item().Text(txt =>
                                {
                                    txt.Span("MD5 (Destination): ").FontSize(9).Bold();
                                    txt.Span(file.DestMd5).FontSize(8).FontFamily("Courier New");
                                });
                            }
                        }

                        // Secondary destination hashes (if available)
                        if (!string.IsNullOrWhiteSpace(secondaryDestinationPath))
                        {
                            if (sha256Enabled && !string.IsNullOrWhiteSpace(file.SecondaryDestSha256))
                            {
                                fileCol.Item().Text(txt =>
                                {
                                    txt.Span("SHA-256 (Secondary): ").FontSize(9).Bold();
                                    txt.Span(file.SecondaryDestSha256).FontSize(8).FontFamily("Courier New");
                                });
                            }

                            if (md5Enabled && !string.IsNullOrWhiteSpace(file.SecondaryDestMd5))
                            {
                                fileCol.Item().Text(txt =>
                                {
                                    txt.Span("MD5 (Secondary): ").FontSize(9).Bold();
                                    txt.Span(file.SecondaryDestMd5).FontSize(8).FontFamily("Courier New");
                                });
                            }
                        }

                        // Timestamps
                        if (!string.IsNullOrWhiteSpace(file.FileDate))
                        {
                            fileCol.Item().PaddingVertical(2);
                            fileCol.Item().Text(txt =>
                            {
                                txt.Span("File Date: ").FontSize(9);
                                txt.Span(file.FileDate).FontSize(9);
                            });
                        }

                        if (!string.IsNullOrWhiteSpace(file.TransferDate))
                        {
                            fileCol.Item().Text(txt =>
                            {
                                txt.Span("Transfer Date: ").FontSize(9);
                                txt.Span(file.TransferDate).FontSize(9);
                            });
                        }
                    });

                    column.Item().PaddingVertical(4);
                }
            });
        }

        void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Column(column =>
            {
                column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                column.Item().Text(txt =>
                {
                    txt.Span("Generated by HashDash v2.0 on ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    txt.Span($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                column.Item().Text(txt =>
                {
                    txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                    txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }
    })
    .GeneratePdf(outputPath);
}


    }
}