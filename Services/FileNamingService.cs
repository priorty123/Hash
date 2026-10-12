using System;
using HashDash;
using HashDash.Helpers;
using HashDash.Services;

namespace HashDash.Services
{
    public class FileNamingService
    {
        /// <summary>
        /// Generates export folder name based on source/destination drives and exhibit info
        /// </summary>
        public string GenerateExportFolderName(
            string sourcePath,
            string destinationPath,
            string computerName,
            string exhibitRef = "")
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string exhibit = FileNameHelper.Sanitize(exhibitRef);
            
            string sourceLabel = DriveInfoHelper.GetDriveLabel(sourcePath, computerName);
            string destLabel = DriveInfoHelper.GetDriveLabel(destinationPath, computerName);

            // Determine direction
            bool sourceIsLocal = sourceLabel.Equals(computerName, StringComparison.OrdinalIgnoreCase);

            string folderName;
            if (sourceIsLocal)
            {
                // From local computer to external
                folderName = $"From_{computerName}_To_{destLabel}";
            }
            else
            {
                // From external to local computer
                folderName = $"From_{sourceLabel}_To_{computerName}";
            }

            if (!string.IsNullOrEmpty(exhibit))
            {
                folderName += $"_{exhibit}";
            }

            folderName += $"_{timestamp}";

            return FileNameHelper.Sanitize(folderName);
        }

        /// <summary>
        /// Generates export file name with timestamp
        /// </summary>
        public string GenerateExportFileName(string extension, string exhibitRef = "")
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string exhibit = FileNameHelper.Sanitize(exhibitRef);

            string fileName = !string.IsNullOrEmpty(exhibit)
                ? $"{exhibit}_{timestamp}"
                : $"HashReport_{timestamp}";

            return $"{fileName}.{extension.TrimStart('.')}";
        }

        /// <summary>
        /// Generates screenshot filename
        /// </summary>
        public string GenerateScreenshotFilename(
            string exhibitRef = "",
            string labCaseRef = "")
        {
            string exhibit = FileNameHelper.Sanitize(exhibitRef);
            string labCase = FileNameHelper.Sanitize(labCaseRef);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            return (!string.IsNullOrEmpty(exhibit) && !string.IsNullOrEmpty(labCase))
                ? $"{exhibit}_{labCase}_{timestamp}"
                : !string.IsNullOrEmpty(exhibit)
                    ? $"{exhibit}_{timestamp}"
                    : !string.IsNullOrEmpty(labCase)
                        ? $"{labCase}_{timestamp}"
                        : $"HashDash_{timestamp}";
        }
    }
}