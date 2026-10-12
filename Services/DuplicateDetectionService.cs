using HashDash.Models;
using System.Collections.Generic;
using System.Linq;

namespace HashDash.Services
{
    public class DuplicateDetectionService
    {
        public Dictionary<string, List<FileHashEntry>> FindDuplicates(IEnumerable<FileHashEntry> files)
        {
            // ✅ FIXED: Return Dictionary instead of List
            return files
                .Where(f => !string.IsNullOrEmpty(f.Sha256))
                .GroupBy(f => f.Sha256)
                .Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key!, g => g.ToList());
        }
    }
}