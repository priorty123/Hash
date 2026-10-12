using HashDash.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace HashDash.Services
{
    public class DatabaseService
    {
        private string _databasePath = "";

        public void Initialize(string databasePath)
        {
            _databasePath = databasePath;

            // Ensure directory exists
            string? directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create database if it doesn't exist
            if (!File.Exists(databasePath))
            {
                CreateDatabase();
            }
            else
            {
                // Validate schema
                ValidateSchema();
            }
        }

        private void CreateDatabase()
        {
            SQLiteConnection.CreateFile(_databasePath);

            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            // Enable WAL mode for better concurrency
            using (var cmd = new SQLiteCommand("PRAGMA journal_mode=WAL;", connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Create ProcessLog table
            string createProcessLog = @"
                CREATE TABLE ProcessLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExaminerName TEXT NOT NULL,
                    LabCaseRef TEXT NOT NULL,
                    ExhibitRef TEXT NOT NULL,
                    ComputerName TEXT NOT NULL,
                    SourcePath TEXT NOT NULL,
                    SourceDriveLabel TEXT,
                    DestinationPath TEXT,
                    DestinationDriveLabel TEXT,
                    SecondaryDestinationPath TEXT,
                    SecondaryDestinationDriveLabel TEXT,
                    Md5Enabled INTEGER NOT NULL,
                    Sha256Enabled INTEGER NOT NULL,
                    FileCount INTEGER NOT NULL,
                    TotalSizeBytes INTEGER NOT NULL,
                    StartDateTime TEXT NOT NULL,
                    EndDateTime TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    ErrorMessage TEXT,
                    AutomationMode INTEGER NOT NULL,
                    PdfReportPath TEXT,
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";

            using (var cmd = new SQLiteCommand(createProcessLog, connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Create FileResults table
            string createFileResults = @"
                CREATE TABLE FileResults (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcessLogId INTEGER NOT NULL,
                    FileName TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    FileSizeBytes INTEGER NOT NULL,
                    Md5Hash TEXT,
                    Sha256Hash TEXT,
                    DestMd5Hash TEXT,
                    DestSha256Hash TEXT,
                    SecondaryDestMd5Hash TEXT,
                    SecondaryDestSha256Hash TEXT,
                    VerificationResult TEXT,
                    FOREIGN KEY (ProcessLogId) REFERENCES ProcessLog(Id)
                );";

            using (var cmd = new SQLiteCommand(createFileResults, connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Create indexes
            using (var cmd = new SQLiteCommand("CREATE INDEX idx_date ON ProcessLog(StartDateTime);", connection))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("CREATE INDEX idx_exhibit ON ProcessLog(ExhibitRef);", connection))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("CREATE INDEX idx_processlog ON FileResults(ProcessLogId);", connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void ValidateSchema()
        {
            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            // Check required tables exist
            using (var cmd = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND (name='ProcessLog' OR name='FileResults');",
                connection))
            using (var reader = cmd.ExecuteReader())
            {
                int tableCount = 0;
                while (reader.Read()) tableCount++;
                if (tableCount != 2)
                    throw new InvalidOperationException("Database schema is invalid. Please delete and recreate the database.");
            }

            // Migrations: add new columns to existing databases without data loss
            AddColumnIfMissing(connection, "ProcessLog",  "SecondaryDestinationPath",       "TEXT");
            AddColumnIfMissing(connection, "ProcessLog",  "SecondaryDestinationDriveLabel",  "TEXT");
            AddColumnIfMissing(connection, "FileResults", "SecondaryDestMd5Hash",            "TEXT");
            AddColumnIfMissing(connection, "FileResults", "SecondaryDestSha256Hash",         "TEXT");
        }

        private static void AddColumnIfMissing(SQLiteConnection connection, string table, string column, string type)
        {
            try
            {
                using var cmd = new SQLiteCommand(
                    $"ALTER TABLE {table} ADD COLUMN {column} {type};", connection);
                cmd.ExecuteNonQuery();
            }
            catch (SQLiteException)
            {
                // Column already exists — ignore
            }
        }

        public int InsertProcessLog(ProcessLog log)
        {
            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            string sql = @"
                INSERT INTO ProcessLog (
                    ExaminerName, LabCaseRef, ExhibitRef, ComputerName,
                    SourcePath, SourceDriveLabel, DestinationPath, DestinationDriveLabel,
                    SecondaryDestinationPath, SecondaryDestinationDriveLabel,
                    Md5Enabled, Sha256Enabled, FileCount, TotalSizeBytes,
                    StartDateTime, EndDateTime, Status, ErrorMessage,
                    AutomationMode, PdfReportPath, CreatedAt
                ) VALUES (
                    @ExaminerName, @LabCaseRef, @ExhibitRef, @ComputerName,
                    @SourcePath, @SourceDriveLabel, @DestinationPath, @DestinationDriveLabel,
                    @SecondaryDestinationPath, @SecondaryDestinationDriveLabel,
                    @Md5Enabled, @Sha256Enabled, @FileCount, @TotalSizeBytes,
                    @StartDateTime, @EndDateTime, @Status, @ErrorMessage,
                    @AutomationMode, @PdfReportPath, @CreatedAt
                );
                SELECT last_insert_rowid();";

            using var cmd = new SQLiteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@ExaminerName", log.ExaminerName);
            cmd.Parameters.AddWithValue("@LabCaseRef", log.LabCaseRef);
            cmd.Parameters.AddWithValue("@ExhibitRef", log.ExhibitRef);
            cmd.Parameters.AddWithValue("@ComputerName", log.ComputerName);
            cmd.Parameters.AddWithValue("@SourcePath", log.SourcePath);
            cmd.Parameters.AddWithValue("@SourceDriveLabel", log.SourceDriveLabel ?? "");
            cmd.Parameters.AddWithValue("@DestinationPath", log.DestinationPath ?? "");
            cmd.Parameters.AddWithValue("@DestinationDriveLabel", log.DestinationDriveLabel ?? "");
            cmd.Parameters.AddWithValue("@SecondaryDestinationPath", log.SecondaryDestinationPath ?? "");
            cmd.Parameters.AddWithValue("@SecondaryDestinationDriveLabel", log.SecondaryDestinationDriveLabel ?? "");
            cmd.Parameters.AddWithValue("@Md5Enabled", log.Md5Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@Sha256Enabled", log.Sha256Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@FileCount", log.FileCount);
            cmd.Parameters.AddWithValue("@TotalSizeBytes", log.TotalSizeBytes);
            cmd.Parameters.AddWithValue("@StartDateTime", log.StartDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("@EndDateTime", log.EndDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("@Status", log.Status);
            cmd.Parameters.AddWithValue("@ErrorMessage", log.ErrorMessage ?? "");
            cmd.Parameters.AddWithValue("@AutomationMode", log.AutomationMode ? 1 : 0);
            cmd.Parameters.AddWithValue("@PdfReportPath", log.PdfReportPath ?? "");
            cmd.Parameters.AddWithValue("@CreatedAt", log.CreatedAt.ToString("O"));

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void InsertFileResults(int processLogId, List<FileHashEntry> files)
        {
            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            using var transaction = connection.BeginTransaction();

            string sql = @"
                INSERT INTO FileResults (
                    ProcessLogId, FileName, FilePath, FileSizeBytes,
                    Md5Hash, Sha256Hash, DestMd5Hash, DestSha256Hash,
                    SecondaryDestMd5Hash, SecondaryDestSha256Hash,
                    VerificationResult
                ) VALUES (
                    @ProcessLogId, @FileName, @FilePath, @FileSizeBytes,
                    @Md5Hash, @Sha256Hash, @DestMd5Hash, @DestSha256Hash,
                    @SecondaryDestMd5Hash, @SecondaryDestSha256Hash,
                    @VerificationResult
                );";

            foreach (var file in files)
            {
                using var cmd = new SQLiteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@ProcessLogId", processLogId);
                cmd.Parameters.AddWithValue("@FileName", file.FileName);
                cmd.Parameters.AddWithValue("@FilePath", file.FullPath);
                cmd.Parameters.AddWithValue("@FileSizeBytes", file.FileSizeBytes);
                cmd.Parameters.AddWithValue("@Md5Hash", file.Md5 ?? "");
                cmd.Parameters.AddWithValue("@Sha256Hash", file.Sha256 ?? "");
                cmd.Parameters.AddWithValue("@DestMd5Hash", file.DestMd5 ?? "");
                cmd.Parameters.AddWithValue("@DestSha256Hash", file.DestSha256 ?? "");
                cmd.Parameters.AddWithValue("@SecondaryDestMd5Hash", file.SecondaryDestMd5 ?? "");
                cmd.Parameters.AddWithValue("@SecondaryDestSha256Hash", file.SecondaryDestSha256 ?? "");
                cmd.Parameters.AddWithValue("@VerificationResult", file.VerificationResult ?? "");
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public List<ProcessLog> GetAllProcessLogs()
        {
            var logs = new List<ProcessLog>();

            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            string sql = "SELECT * FROM ProcessLog ORDER BY StartDateTime DESC;";

            using var cmd = new SQLiteCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                logs.Add(ReadProcessLogFromReader(reader));
            }

            return logs;
        }

        public List<ProcessLog> GetProcessLogsByDateRange(DateTime startDate, DateTime endDate)
        {
            var logs = new List<ProcessLog>();

            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            string sql = @"
                SELECT * FROM ProcessLog 
                WHERE StartDateTime BETWEEN @StartDate AND @EndDate
                ORDER BY StartDateTime DESC;";

            using var cmd = new SQLiteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("O"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("O"));

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                logs.Add(ReadProcessLogFromReader(reader));
            }

            return logs;
        }

        public List<FileResult> GetFileResults(int processLogId)
        {
            var results = new List<FileResult>();

            using var connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            connection.Open();

            string sql = "SELECT * FROM FileResults WHERE ProcessLogId = @ProcessLogId;";

            using var cmd = new SQLiteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@ProcessLogId", processLogId);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                results.Add(new FileResult
                {
                    Id                     = reader.GetInt32(reader.GetOrdinal("Id")),
                    ProcessLogId           = reader.GetInt32(reader.GetOrdinal("ProcessLogId")),
                    FileName               = reader.GetString(reader.GetOrdinal("FileName")),
                    FilePath               = reader.GetString(reader.GetOrdinal("FilePath")),
                    FileSizeBytes          = reader.GetInt64(reader.GetOrdinal("FileSizeBytes")),
                    Md5Hash                = NullableString(reader, "Md5Hash"),
                    Sha256Hash             = NullableString(reader, "Sha256Hash"),
                    DestMd5Hash            = NullableString(reader, "DestMd5Hash"),
                    DestSha256Hash         = NullableString(reader, "DestSha256Hash"),
                    SecondaryDestMd5Hash   = NullableString(reader, "SecondaryDestMd5Hash"),
                    SecondaryDestSha256Hash= NullableString(reader, "SecondaryDestSha256Hash"),
                    VerificationResult     = NullableString(reader, "VerificationResult") ?? ""
                });
            }

            return results;
        }

        private ProcessLog ReadProcessLogFromReader(SQLiteDataReader reader)
        {
            return new ProcessLog
            {
                Id                            = reader.GetInt32(reader.GetOrdinal("Id")),
                ExaminerName                  = reader.GetString(reader.GetOrdinal("ExaminerName")),
                LabCaseRef                    = reader.GetString(reader.GetOrdinal("LabCaseRef")),
                ExhibitRef                    = reader.GetString(reader.GetOrdinal("ExhibitRef")),
                ComputerName                  = reader.GetString(reader.GetOrdinal("ComputerName")),
                SourcePath                    = reader.GetString(reader.GetOrdinal("SourcePath")),
                SourceDriveLabel              = NullableString(reader, "SourceDriveLabel") ?? "",
                DestinationPath               = NullableString(reader, "DestinationPath") ?? "",
                DestinationDriveLabel         = NullableString(reader, "DestinationDriveLabel") ?? "",
                SecondaryDestinationPath      = NullableString(reader, "SecondaryDestinationPath") ?? "",
                SecondaryDestinationDriveLabel= NullableString(reader, "SecondaryDestinationDriveLabel") ?? "",
                Md5Enabled                    = reader.GetInt32(reader.GetOrdinal("Md5Enabled")) == 1,
                Sha256Enabled                 = reader.GetInt32(reader.GetOrdinal("Sha256Enabled")) == 1,
                FileCount                     = reader.GetInt32(reader.GetOrdinal("FileCount")),
                TotalSizeBytes                = reader.GetInt64(reader.GetOrdinal("TotalSizeBytes")),
                StartDateTime                 = DateTime.Parse(reader.GetString(reader.GetOrdinal("StartDateTime"))),
                EndDateTime                   = DateTime.Parse(reader.GetString(reader.GetOrdinal("EndDateTime"))),
                Status                        = reader.GetString(reader.GetOrdinal("Status")),
                ErrorMessage                  = NullableString(reader, "ErrorMessage"),
                AutomationMode                = reader.GetInt32(reader.GetOrdinal("AutomationMode")) == 1,
                PdfReportPath                 = NullableString(reader, "PdfReportPath"),
                CreatedAt                     = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")))
            };
        }

        private static string? NullableString(SQLiteDataReader reader, string column)
        {
            int ord = reader.GetOrdinal(column);
            return reader.IsDBNull(ord) ? null : reader.GetString(ord);
        }
    }
}