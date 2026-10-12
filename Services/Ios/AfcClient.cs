using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HashDash.Services.Ios
{
    /// <summary>
    /// Apple File Conduit client. Only the read-side operations are implemented —
    /// there is no write, remove, rename, mkdir, truncate or set-time operation here,
    /// and files are only ever opened in read-only mode.
    /// </summary>
    public sealed class AfcClient : IDisposable
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CFA6LPAA");
        private const int HeaderSize = 40;

        private const ulong OpStatus      = 0x01;
        private const ulong OpData        = 0x02;
        private const ulong OpReadDir     = 0x03;
        private const ulong OpGetFileInfo = 0x0A;
        private const ulong OpFileOpen    = 0x0D;
        private const ulong OpFileOpenRes = 0x0E;
        private const ulong OpFileRead    = 0x0F;
        private const ulong OpFileClose   = 0x14;

        private const ulong ModeReadOnly  = 0x01;

        /// <summary>Largest single read request; larger reads are split.</summary>
        public const int MaxReadSize = 1024 * 1024;

        private readonly Stream       _stream;
        private readonly IDisposable  _owner;
        private ulong _packetNumber;

        public AfcClient(Stream stream, IDisposable owner)
        {
            _stream = stream;
            _owner  = owner;
        }

        public List<string> ReadDirectory(string path)
        {
            byte[] reply = Send(OpReadDir, PathBytes(path), OpData);
            return SplitStrings(reply);
        }

        /// <summary>Returns st_* keys (st_size, st_ifmt, st_mtime, st_birthtime, ...), or null if the path does not exist / is not readable.</summary>
        public Dictionary<string, string>? GetFileInfo(string path)
        {
            byte[] reply;
            try
            {
                reply = Send(OpGetFileInfo, PathBytes(path), OpData);
            }
            catch (AfcException)
            {
                return null;
            }

            var parts = SplitStrings(reply);
            var info  = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < parts.Count; i += 2)
                info[parts[i]] = parts[i + 1];
            return info;
        }

        public ulong OpenReadOnly(string path)
        {
            byte[] path8 = PathBytes(path);
            byte[] data  = new byte[8 + path8.Length];
            BinaryPrimitives.WriteUInt64LittleEndian(data, ModeReadOnly);
            path8.CopyTo(data, 8);

            byte[] reply = Send(OpFileOpen, data, OpFileOpenRes);
            return BinaryPrimitives.ReadUInt64LittleEndian(reply);
        }

        public int Read(ulong handle, byte[] buffer, int offset, int count)
        {
            byte[] data = new byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(data,           handle);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), (ulong)Math.Min(count, MaxReadSize));

            byte[] reply = Send(OpFileRead, data, OpData);
            Buffer.BlockCopy(reply, 0, buffer, offset, reply.Length);
            return reply.Length;
        }

        public void Close(ulong handle)
        {
            byte[] data = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(data, handle);
            Send(OpFileClose, data, OpStatus);
        }

        private byte[] Send(ulong operation, byte[] data, ulong expectedReply)
        {
            byte[] header = new byte[HeaderSize];
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8),  (ulong)(HeaderSize + data.Length)); // entire length
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), (ulong)(HeaderSize + data.Length)); // this length
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24), _packetNumber++);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), operation);
            _stream.Write(header);
            _stream.Write(data);
            _stream.Flush();

            byte[] replyHeader = StreamUtil.ReadExactly(_stream, HeaderSize);
            if (!replyHeader.AsSpan(0, 8).SequenceEqual(Magic))
                throw new InvalidDataException("Malformed AFC reply.");

            ulong entire = BinaryPrimitives.ReadUInt64LittleEndian(replyHeader.AsSpan(8));
            ulong op     = BinaryPrimitives.ReadUInt64LittleEndian(replyHeader.AsSpan(32));
            if (entire < HeaderSize || entire > (ulong)(MaxReadSize + 1024 * 1024))
                throw new InvalidDataException("Malformed AFC reply length.");

            byte[] body = StreamUtil.ReadExactly(_stream, (int)entire - HeaderSize);

            if (op == OpStatus)
            {
                ulong status = body.Length >= 8 ? BinaryPrimitives.ReadUInt64LittleEndian(body) : 0;
                if (status != 0) throw new AfcException(status);
                if (expectedReply == OpStatus) return body;
                throw new AfcException(status);
            }

            if (op != expectedReply)
                throw new InvalidDataException($"Unexpected AFC reply operation 0x{op:X}.");
            return body;
        }

        private static byte[] PathBytes(string path)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(path);
            Array.Resize(ref utf8, utf8.Length + 1); // null terminator
            return utf8;
        }

        private static List<string> SplitStrings(byte[] data)
        {
            var result = new List<string>();
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0) continue;
                if (i > start) result.Add(Encoding.UTF8.GetString(data, start, i - start));
                start = i + 1;
            }
            if (start < data.Length) result.Add(Encoding.UTF8.GetString(data, start, data.Length - start));
            return result;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _owner.Dispose();
        }
    }

    public sealed class AfcException : IOException
    {
        public ulong Status { get; }

        public AfcException(ulong status) : base(Describe(status)) => Status = status;

        private static string Describe(ulong status) => status switch
        {
            8  => "File or folder not found on the device.",
            10 => "Permission denied by the device.",
            7  => "Invalid path.",
            _  => $"The device returned AFC error {status}."
        };
    }
}
