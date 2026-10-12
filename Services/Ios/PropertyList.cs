using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HashDash.Services.Ios
{
    /// <summary>
    /// Minimal Apple property-list support: XML plists (read and write — the format
    /// usbmuxd and lockdown speak) and binary bplist00 (read only — pair records can be
    /// stored either way). Values map to: Dictionary&lt;string, object?&gt;, List&lt;object?&gt;,
    /// string, long, double, bool, byte[], DateTime.
    /// </summary>
    public static class PropertyList
    {
        private static readonly DateTime AppleEpoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── Parse ─────────────────────────────────────────────────────────

        public static object? Parse(byte[] bytes)
        {
            if (bytes.Length >= 8 && Encoding.ASCII.GetString(bytes, 0, 8) == "bplist00")
                return new BinaryReader(bytes).ReadRoot();

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            var doc  = XDocument.Load(reader);
            var root = doc.Root?.Elements().FirstOrDefault()
                ?? throw new InvalidDataException("Empty property list.");
            return ParseXml(root);
        }

        public static Dictionary<string, object?> ParseDictionary(byte[] bytes) =>
            Parse(bytes) as Dictionary<string, object?>
                ?? throw new InvalidDataException("Property list root is not a dictionary.");

        private static object? ParseXml(XElement e) => e.Name.LocalName switch
        {
            "dict"    => ParseXmlDict(e),
            "array"   => e.Elements().Select(ParseXml).ToList(),
            "string"  => e.Value,
            "integer" => long.Parse(e.Value.Trim(), CultureInfo.InvariantCulture),
            "real"    => double.Parse(e.Value.Trim(), CultureInfo.InvariantCulture),
            "true"    => true,
            "false"   => false,
            "data"    => Convert.FromBase64String(new string(e.Value.Where(c => !char.IsWhiteSpace(c)).ToArray())),
            "date"    => DateTime.Parse(e.Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            _         => null
        };

        private static Dictionary<string, object?> ParseXmlDict(XElement e)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            var children = e.Elements().ToList();
            for (int i = 0; i + 1 < children.Count; i += 2)
                dict[children[i].Value] = ParseXml(children[i + 1]);
            return dict;
        }

        // ── Write (XML) ───────────────────────────────────────────────────

        public static byte[] ToXml(Dictionary<string, object?> root)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
                new XElement("plist", new XAttribute("version", "1.0"), ToXmlElement(root)));

            using var ms = new MemoryStream();
            using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
                doc.Save(writer);
            return ms.ToArray();
        }

        private static XElement ToXmlElement(object? value) => value switch
        {
            Dictionary<string, object?> d => new XElement("dict",
                d.SelectMany(kv => new[] { new XElement("key", kv.Key), ToXmlElement(kv.Value) })),
            IEnumerable<object?> list when value is not string => new XElement("array", list.Select(ToXmlElement)),
            string s   => new XElement("string", s),
            bool b     => new XElement(b ? "true" : "false"),
            int i      => new XElement("integer", i.ToString(CultureInfo.InvariantCulture)),
            long l     => new XElement("integer", l.ToString(CultureInfo.InvariantCulture)),
            double d   => new XElement("real", d.ToString("R", CultureInfo.InvariantCulture)),
            byte[] b   => new XElement("data", Convert.ToBase64String(b)),
            DateTime t => new XElement("date", t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            _          => throw new NotSupportedException($"Cannot write {value?.GetType().Name ?? "null"} to a property list.")
        };

        // ── Typed accessors ───────────────────────────────────────────────

        public static string? GetString(this Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) ? v as string : null;

        public static long? GetInteger(this Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v is long l ? l : null;

        public static bool GetBool(this Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v is true;

        public static byte[]? GetData(this Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) ? v as byte[] : null;

        public static Dictionary<string, object?>? GetDict(this Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) ? v as Dictionary<string, object?> : null;

        // ── Binary (bplist00) reader ──────────────────────────────────────

        private sealed class BinaryReader
        {
            private readonly byte[] _b;
            private readonly int    _offsetSize;
            private readonly int    _refSize;
            private readonly long[] _offsets;
            private readonly long   _root;

            public BinaryReader(byte[] bytes)
            {
                _b = bytes;
                int t = bytes.Length - 32;
                _offsetSize = bytes[t + 6];
                _refSize    = bytes[t + 7];
                long count  = ReadBE(t + 8, 8);
                _root       = ReadBE(t + 16, 8);
                long table  = ReadBE(t + 24, 8);

                _offsets = new long[count];
                for (long i = 0; i < count; i++)
                    _offsets[i] = ReadBE((int)(table + i * _offsetSize), _offsetSize);
            }

            public object? ReadRoot() => ReadObject(_root, 0);

            private long ReadBE(int pos, int size)
            {
                long v = 0;
                for (int i = 0; i < size; i++) v = (v << 8) | _b[pos + i];
                return v;
            }

            private (long length, int start) ReadLength(int pos, int marker)
            {
                if (marker != 0xF) return (marker, pos + 1);
                int intMarker = _b[pos + 1];
                int size      = 1 << (intMarker & 0xF);
                return (ReadBE(pos + 2, size), pos + 2 + size);
            }

            private object? ReadObject(long index, int depth)
            {
                if (depth > 64) throw new InvalidDataException("Property list nested too deeply.");

                int pos    = (int)_offsets[index];
                int type   = _b[pos] >> 4;
                int marker = _b[pos] & 0xF;

                switch (type)
                {
                    case 0x0:
                        return marker switch { 0x8 => false, 0x9 => true, _ => null };
                    case 0x1:
                        return ReadBE(pos + 1, 1 << marker);
                    case 0x2:
                    {
                        int size = 1 << marker;
                        var raw = _b.Skip(pos + 1).Take(size).Reverse().ToArray();
                        return size == 4 ? BitConverter.ToSingle(raw, 0) : BitConverter.ToDouble(raw, 0);
                    }
                    case 0x3:
                    {
                        var raw = _b.Skip(pos + 1).Take(8).Reverse().ToArray();
                        return AppleEpoch.AddSeconds(BitConverter.ToDouble(raw, 0));
                    }
                    case 0x4:
                    {
                        var (len, start) = ReadLength(pos, marker);
                        return _b.Skip(start).Take((int)len).ToArray();
                    }
                    case 0x5:
                    {
                        var (len, start) = ReadLength(pos, marker);
                        return Encoding.ASCII.GetString(_b, start, (int)len);
                    }
                    case 0x6:
                    {
                        var (len, start) = ReadLength(pos, marker);
                        return Encoding.BigEndianUnicode.GetString(_b, start, (int)len * 2);
                    }
                    case 0x8:
                        return ReadBE(pos + 1, marker + 1);
                    case 0xA:
                    {
                        var (len, start) = ReadLength(pos, marker);
                        var list = new List<object?>((int)len);
                        for (int i = 0; i < len; i++)
                            list.Add(ReadObject(ReadBE(start + i * _refSize, _refSize), depth + 1));
                        return list;
                    }
                    case 0xD:
                    {
                        var (len, start) = ReadLength(pos, marker);
                        var dict = new Dictionary<string, object?>((int)len, StringComparer.Ordinal);
                        for (int i = 0; i < len; i++)
                        {
                            string key = ReadObject(ReadBE(start + i * _refSize, _refSize), depth + 1) as string ?? "";
                            dict[key] = ReadObject(ReadBE(start + (int)(len + i) * _refSize, _refSize), depth + 1);
                        }
                        return dict;
                    }
                    default:
                        throw new InvalidDataException($"Unsupported binary plist object type 0x{type:X}.");
                }
            }
        }
    }
}
