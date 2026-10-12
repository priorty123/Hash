using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace HashDash.Services
{
    public class PhotoLibraryAsset
    {
        public long   Id { get; init; }
        /// <summary>Media-root relative path of the original, e.g. DCIM/100APPLE/IMG_0001.HEIC</summary>
        public string RelativePath { get; init; } = "";
        public string? OriginalFileName { get; init; }
        public DateTime? Created { get; init; }
        /// <summary>Every Photos-app grouping the asset appears under, as "Section/Name" (e.g. "Albums/WhatsApp", "Saved by App/Snapchat").</summary>
        public List<string> Groups { get; } = new();
        public string? ImportedBy { get; set; }
    }

    /// <summary>How a device file relates to the Photos library.</summary>
    public class PhotoLibraryMatch
    {
        public PhotoLibraryAsset Asset { get; init; } = null!;
        public string Role { get; init; } = "";
    }

    /// <summary>
    /// Rebuilds the grouping the iOS Photos app shows — user/app albums (with their folders),
    /// "Saved by App" (the app that wrote each item into the library, even when it never
    /// created an album), media types and utilities — from a working copy of
    /// PhotoData/Photos.sqlite, and maps those groups onto the files found on the device.
    ///
    /// The Core Data schema renumbers its join tables between iOS versions (e.g. Z_26ASSETS,
    /// Z_34ASSETS), so table and column names are discovered at runtime instead of hard-coded.
    /// </summary>
    public class IosPhotoLibraryService
    {
        public const string SectionAlbums     = "Albums";
        public const string SectionSavedByApp = "Saved by App";
        public const string SectionMediaTypes = "Media Types";
        public const string SectionUtilities  = "Utilities";

        private static readonly DateTime CoreDataEpoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly Dictionary<string, PhotoLibraryAsset> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PhotoLibraryAsset> _byStem = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<PhotoLibraryAsset> Assets => _byPath.Values;

        /// <summary>Assets the library lists whose original is not on the device (iCloud "Optimise Storage", shared items not downloaded).</summary>
        public List<PhotoLibraryAsset> MissingOnDevice { get; } = new();

        public static IosPhotoLibraryService Load(string sqlitePath)
        {
            var library = new IosPhotoLibraryService();
            using var db = new SQLiteConnection($"Data Source={sqlitePath};Read Only=False;Journal Mode=WAL");
            db.Open();
            library.LoadAssets(db);
            library.LoadAlbums(db);
            return library;
        }

        // ── Loading ───────────────────────────────────────────────────────

        private void LoadAssets(SQLiteConnection db)
        {
            string assetTable = TableExists(db, "ZASSET") ? "ZASSET" : "ZGENERICASSET"; // pre-iOS 14 name
            var cols = Columns(db, assetTable);

            string Col(string name, string fallback = "NULL") => cols.Contains(name) ? $"a.{name}" : fallback;

            bool hasAttributes = TableExists(db, "ZADDITIONALASSETATTRIBUTES");
            var attrCols = hasAttributes ? Columns(db, "ZADDITIONALASSETATTRIBUTES") : new HashSet<string>();
            string Attr(string name) => attrCols.Contains(name) ? $"x.{name}" : "NULL";

            string sql = $@"
                SELECT a.Z_PK, a.ZDIRECTORY, a.ZFILENAME,
                       {Col("ZHIDDEN", "0")}, {Col("ZTRASHEDSTATE", "0")}, {Col("ZFAVORITE", "0")},
                       {Col("ZKIND", "0")}, {Col("ZKINDSUBTYPE", "0")}, {Col("ZDEPTHTYPE", "0")},
                       {Col("ZDATECREATED")},
                       {Attr("ZORIGINALFILENAME")}, {Attr("ZIMPORTEDBYDISPLAYNAME")}, {Attr("ZIMPORTEDBYBUNDLEIDENTIFIER")}
                FROM {assetTable} a
                {(hasAttributes ? "LEFT JOIN ZADDITIONALASSETATTRIBUTES x ON x.ZASSET = a.Z_PK" : "")}
                WHERE a.ZFILENAME IS NOT NULL";

            var appNames  = new Dictionary<string, string>(StringComparer.Ordinal);
            var importers = new Dictionary<PhotoLibraryAsset, string?>();

            using var cmd    = new SQLiteCommand(sql, db);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string directory = (reader.IsDBNull(1) ? "" : reader.GetString(1)).Replace('\\', '/').Trim('/');
                string fileName  = reader.GetString(2);
                const string mediaPrefix = "var/mobile/Media/";
                if (directory.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase))
                    directory = directory.Substring(mediaPrefix.Length);

                // Originals live under DCIM; iCloud-synced and shared ones under PhotoData.
                if (!directory.StartsWith("DCIM", StringComparison.OrdinalIgnoreCase) &&
                    !directory.StartsWith("PhotoData", StringComparison.OrdinalIgnoreCase))
                    directory = "PhotoData/" + directory;

                string? displayName = Clean(GetString(reader, 11));
                string? bundleId    = Clean(GetString(reader, 12));
                if (displayName != null && bundleId != null)
                    appNames.TryAdd(bundleId, displayName);

                var asset = new PhotoLibraryAsset
                {
                    Id               = reader.GetInt64(0),
                    RelativePath     = directory + "/" + fileName,
                    OriginalFileName = GetString(reader, 10),
                    Created          = reader.IsDBNull(9) ? null : CoreDataEpoch.AddSeconds(reader.GetDouble(9)).ToLocalTime(),
                    ImportedBy       = displayName ?? bundleId
                };
                importers[asset] = bundleId;

                string? mediaType = MediaType(GetLong(reader, 6), GetLong(reader, 7));
                if (mediaType != null) asset.Groups.Add($"{SectionMediaTypes}/{mediaType}");
                if (GetLong(reader, 8) > 0) asset.Groups.Add($"{SectionMediaTypes}/Portrait");

                if (GetLong(reader, 5) == 1) asset.Groups.Add($"{SectionAlbums}/Favourites");
                if (GetLong(reader, 3) == 1) asset.Groups.Add($"{SectionUtilities}/Hidden");
                if (GetLong(reader, 4) == 1) asset.Groups.Add($"{SectionUtilities}/Recently Deleted");

                _byPath[asset.RelativePath] = asset;
                _byStem.TryAdd(StemKey(directory, fileName), asset);
            }

            // Some rows carry only the bundle ID; name them after the display name other
            // rows give the same app, so each app appears once under "Saved by App".
            foreach (var (asset, bundleId) in importers)
            {
                if (bundleId != null && appNames.TryGetValue(bundleId, out string? name))
                    asset.ImportedBy = name;
                if (bundleId == "com.apple.springboard")
                    asset.ImportedBy = "Screenshots (SpringBoard)";

                if (asset.ImportedBy != null)
                    asset.Groups.Insert(0, $"{SectionSavedByApp}/{asset.ImportedBy}");
            }
        }

        private void LoadAlbums(SQLiteConnection db)
        {
            if (!TableExists(db, "ZGENERICALBUM")) return;

            // Join table: the one whose columns are exactly Z_<n>ALBUMS / Z_<m>ASSETS
            string? joinTable = null, albumCol = null, assetCol = null;
            foreach (string table in Tables(db).Where(t => t.StartsWith("Z_") && t.EndsWith("ASSETS")))
            {
                var cols = Columns(db, table);
                string? a = cols.FirstOrDefault(c => IsNumbered(c, "ALBUMS"));
                string? s = cols.FirstOrDefault(c => IsNumbered(c, "ASSETS"));
                if (a != null && s != null) { joinTable = table; albumCol = a; assetCol = s; break; }
            }
            if (joinTable == null) return;

            var albums = new Dictionary<long, (string title, long kind, long? parent)>();
            using (var cmd = new SQLiteCommand(
                "SELECT Z_PK, ZTITLE, ZKIND, ZPARENTFOLDER FROM ZGENERICALBUM WHERE ZTRASHEDSTATE IS NULL OR ZTRASHEDSTATE = 0", db))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    albums[r.GetInt64(0)] = (GetString(r, 1) ?? "", GetLong(r, 2), r.IsDBNull(3) ? null : r.GetInt64(3));
            }

            var byId    = _byPath.Values.ToDictionary(a => a.Id);
            var members = new Dictionary<long, List<PhotoLibraryAsset>>();
            using (var join = new SQLiteCommand($"SELECT {albumCol}, {assetCol} FROM {joinTable}", db))
            using (var jr = join.ExecuteReader())
            {
                while (jr.Read())
                {
                    if (!byId.TryGetValue(jr.GetInt64(1), out var asset)) continue;
                    long albumId = jr.GetInt64(0);
                    if (!members.TryGetValue(albumId, out var list)) members[albumId] = list = new();
                    list.Add(asset);
                }
            }

            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, album) in albums.OrderBy(a => a.Key))
            {
                // kind 2 = regular/app album, 1505 = shared album; 4000 = folder (used for the path)
                if (album.kind is not (2 or 1505) || !members.TryGetValue(id, out var assets)) continue;

                string section = album.kind == 1505 ? "Shared Albums" : SectionAlbums;
                string title   = string.IsNullOrWhiteSpace(album.title) ? "(Untitled)" : album.title.Trim();
                string key     = $"{section}/{string.Join("/", FolderChain(albums, album.parent).Append(title))}";

                // Two non-empty albums can share a name (e.g. two "Exif Edited"); keep them apart.
                used[key] = used.TryGetValue(key, out int n) ? n + 1 : 1;
                string group = used[key] == 1 ? key : $"{key} ({used[key]})";

                foreach (var asset in assets)
                    asset.Groups.Add(group);
            }
        }

        private static IEnumerable<string> FolderChain(Dictionary<long, (string title, long kind, long? parent)> albums, long? parent)
        {
            var chain = new List<string>();
            for (int depth = 0; parent != null && depth < 16 && albums.TryGetValue(parent.Value, out var folder); depth++)
            {
                if (folder.kind != 4000) break; // 3999 = invisible root folder
                chain.Insert(0, string.IsNullOrWhiteSpace(folder.title) ? "(Untitled folder)" : folder.title.Trim());
                parent = folder.parent;
            }
            return chain;
        }

        // ── Matching device files to the library ──────────────────────────

        /// <summary>
        /// Finds the library asset a device file belongs to: the original itself, its Live
        /// Photo video, an edited render (PhotoData/Mutations), its adjustment sidecar or its
        /// thumbnail. Returns null for files with no library relationship.
        /// </summary>
        public PhotoLibraryMatch? Match(string relativePath)
        {
            string path = relativePath.Replace('\\', '/').Trim('/');

            if (_byPath.TryGetValue(path, out var original))
                return new PhotoLibraryMatch { Asset = original, Role = "Original" };

            string[] parts = path.Split('/');
            string file    = parts[^1];
            string ext     = Path.GetExtension(file).ToUpperInvariant();

            // DCIM/100APPLE/IMG_0001.MOV (Live Photo video), IMG_E0001.JPG (edited copy), IMG_0001.AAE
            if (parts.Length >= 3 && parts[0].Equals("DCIM", StringComparison.OrdinalIgnoreCase))
            {
                string dir  = string.Join("/", parts[..^1]);
                string stem = Path.GetFileNameWithoutExtension(file);
                bool edited = stem.StartsWith("IMG_E", StringComparison.OrdinalIgnoreCase);
                if (edited) stem = "IMG_" + stem.Substring(5);

                if (_byStem.TryGetValue(dir + "/" + stem, out var asset))
                    return new PhotoLibraryMatch
                    {
                        Asset = asset,
                        Role  = edited ? "Edited copy" : ext == ".AAE" ? "Adjustment sidecar" : ext == ".MOV" ? "Live Photo video" : "Related file"
                    };
            }

            // PhotoData/Mutations/DCIM/100APPLE/IMG_0001/Adjustments/FullSizeRender.jpg
            if (parts.Length >= 6 && Is(parts, 0, "PhotoData") && Is(parts, 1, "Mutations") &&
                _byStem.TryGetValue($"{parts[2]}/{parts[3]}/{parts[4]}", out var mutated))
                return new PhotoLibraryMatch { Asset = mutated, Role = "Edited render" };

            // PhotoData/Thumbnails/V2/DCIM/100APPLE/IMG_0001.HEIC/5005.JPG
            if (parts.Length >= 7 && Is(parts, 0, "PhotoData") && Is(parts, 1, "Thumbnails") &&
                _byPath.TryGetValue($"{parts[3]}/{parts[4]}/{parts[5]}", out var thumbOf))
                return new PhotoLibraryMatch { Asset = thumbOf, Role = "Thumbnail" };

            // PhotoData/Metadata/DCIM/100APPLE/IMG_0001.AAE
            if (parts.Length >= 5 && Is(parts, 0, "PhotoData") && Is(parts, 1, "Metadata") &&
                _byStem.TryGetValue($"{parts[2]}/{parts[3]}/{Path.GetFileNameWithoutExtension(parts[4])}", out var meta))
                return new PhotoLibraryMatch { Asset = meta, Role = "Adjustment sidecar" };

            return null;
        }

        /// <summary>Records which library originals were not found among the device files.</summary>
        public void ResolveMissing(IEnumerable<string> devicePaths)
        {
            var present = new HashSet<string>(devicePaths.Select(p => p.Replace('\\', '/').Trim('/')), StringComparer.OrdinalIgnoreCase);
            MissingOnDevice.Clear();
            MissingOnDevice.AddRange(_byPath.Values.Where(a => !present.Contains(a.RelativePath)));
        }

        /// <summary>Thumbnail the Photos app keeps for an original — a small JPEG, much cheaper to read than the original.</summary>
        public static string ThumbnailPathFor(string originalRelativePath) =>
            $"PhotoData/Thumbnails/V2/{originalRelativePath.Replace('\\', '/').Trim('/')}/5005.JPG";

        // ── Helpers ───────────────────────────────────────────────────────

        private static string? MediaType(long kind, long subtype) => (kind, subtype) switch
        {
            (0, 1)   => "Panoramas",
            (0, 2)   => "Live Photos",
            (0, 10)  => "Screenshots",
            (1, 101) => "Slo-mo",
            (1, 102) => "Time-lapse",
            (1, 103) => "Screen Recordings",
            (1, _)   => "Videos",
            _        => null
        };

        /// <summary>Display names carry invisible direction marks (e.g. "‎WhatsApp").</summary>
        private static string? Clean(string? value)
        {
            if (value == null) return null;
            string cleaned = new string(value.Where(c => c is not ('‎' or '‏' or '‪' or '‬')).ToArray()).Trim();
            return cleaned.Length == 0 ? null : cleaned;
        }

        private static string StemKey(string directory, string fileName) =>
            directory + "/" + Path.GetFileNameWithoutExtension(fileName);

        private static bool Is(string[] parts, int index, string value) =>
            parts[index].Equals(value, StringComparison.OrdinalIgnoreCase);

        private static bool IsNumbered(string column, string suffix) =>
            column.StartsWith("Z_") && column.EndsWith(suffix) &&
            column.Substring(2, column.Length - 2 - suffix.Length).All(char.IsDigit);

        private static string? GetString(SQLiteDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));

        private static long GetLong(SQLiteDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));

        private static bool TableExists(SQLiteConnection db, string table) => Tables(db).Contains(table);

        private static HashSet<string> Tables(SQLiteConnection db)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type = 'table'", db);
            using var r   = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(0));
            return set;
        }

        private static HashSet<string> Columns(SQLiteConnection db, string table)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = new SQLiteCommand($"PRAGMA table_info('{table.Replace("'", "''")}')", db);
            using var r   = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(1));
            return set;
        }
    }
}
