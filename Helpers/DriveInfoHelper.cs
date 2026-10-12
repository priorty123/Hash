using System;
using System.IO;

namespace HashDash.Helpers
{
    public static class DriveInfoHelper
    {
        /// <summary>
        /// Gets the drive label for a given path. Returns computer name for system drive, drive label for others.
        /// </summary>
        public static string GetDriveLabel(string path, string computerName)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";

            try
            {
                string? root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return "";

                var drive = new DriveInfo(root);
                bool isSystemDrive = string.Equals(
                    root,
                    Path.GetPathRoot(Environment.SystemDirectory),
                    StringComparison.OrdinalIgnoreCase);

                if (isSystemDrive)
                {
                    return computerName;
                }
                else
                {
                    return string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name.TrimEnd('\\')
                        : drive.VolumeLabel;
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Formats drive label for display with pipe separator
        /// </summary>
        public static string FormatDriveLabelForDisplay(string path, string computerName)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            try
            {
                // ✅ HANDLE NETWORK PATHS
                if (path.StartsWith(@"\\"))
                {
                    // UNC path
                    return $"(Network: {path.Split('\\')[2]})";
                }

                // Local path
                var drive = new DriveInfo(Path.GetPathRoot(path) ?? "");
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name})";
                return $"({label})";
            }
            catch
            {
                return "(Unknown)";
            }
        }

        /// <summary>
        /// Checks if there's enough disk space at the destination
        /// </summary>
        public static (bool hasSpace, long requiredMb, long freeMb) CheckDiskSpace(string path, long requiredBytes)
        {
            try
            {
                long reqMb = requiredBytes / (1024 * 1024);

                // ✅ HANDLE NETWORK PATHS
                if (path.StartsWith(@"\\"))
                {
                    // For network paths, use GetDiskFreeSpaceEx
                    string rootPath = Path.GetPathRoot(path) ?? path;

                    if (NativeMethods.GetDiskFreeSpaceEx(
                        rootPath,
                        out long freeBytes,
                        out _,
                        out _))
                    {
                        long availMb = freeBytes / (1024 * 1024);
                        return (freeBytes >= requiredBytes, reqMb, availMb);
                    }

                    // If API call fails, assume space is available
                    return (true, reqMb, 0);
                }

                // Local path - use DriveInfo
                var drive = new DriveInfo(Path.GetPathRoot(path) ?? "");
                long driveMb = drive.AvailableFreeSpace / (1024 * 1024);
                return (drive.AvailableFreeSpace >= requiredBytes, reqMb, driveMb);
            }
            catch
            {
                return (true, 0, 0); // Assume OK if check fails
            }
        }
    }
}