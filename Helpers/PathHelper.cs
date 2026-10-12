using System;
using System.IO;

namespace HashDash.Helpers
{
    public static class PathHelper
    {
        /// <summary>
        /// Gets the relative path from a base folder to a file
        /// </summary>
        public static string GetRelativePath(string filePath, string baseFolderPath)
        {
            if (string.IsNullOrEmpty(baseFolderPath))
                return Path.GetFileName(filePath);

            try
            {
                // Ensure paths end with directory separator
                string baseFolder = baseFolderPath.TrimEnd(
                    Path.DirectorySeparatorChar, 
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (filePath.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return filePath.Substring(baseFolder.Length);
                }
            }
            catch
            {
                // Fall back to filename if calculation fails
            }

            return Path.GetFileName(filePath);
        }
    }
}