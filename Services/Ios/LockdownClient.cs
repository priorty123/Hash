using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HashDash.Services.Ios
{
    /// <summary>
    /// Pairing material Windows already holds for a device that has trusted this PC.
    /// HashDash only ever reads a pair record; it never creates one.
    /// </summary>
    public sealed class PairRecord
    {
        public string HostId     { get; init; } = "";
        public string SystemBuid { get; init; } = "";
        public X509Certificate2 HostCertificate { get; init; } = null!;
        public byte[] DeviceCertificatePem { get; init; } = Array.Empty<byte>();

        public static PairRecord? Load(string udid)
        {
            var raw = TryReadFromService(udid) ?? TryReadFromDisk(udid);
            if (raw == null) return null;

            string? hostId = raw.GetString("HostID");
            byte[]? cert   = raw.GetData("HostCertificate");
            byte[]? key    = raw.GetData("HostPrivateKey");
            if (hostId == null || cert == null || key == null) return null;

            // SChannel cannot use an ephemeral PEM key for client authentication;
            // round-tripping through PKCS#12 gives it a usable key container.
            using var pem = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(cert), Encoding.ASCII.GetString(key));
            var usable = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);

            return new PairRecord
            {
                HostId               = hostId,
                SystemBuid           = raw.GetString("SystemBUID") ?? "",
                HostCertificate      = usable,
                DeviceCertificatePem = raw.GetData("DeviceCertificate") ?? Array.Empty<byte>()
            };
        }

        private static Dictionary<string, object?>? TryReadFromService(string udid)
        {
            try { return UsbmuxClient.ReadPairRecord(udid); }
            catch { return null; }
        }

        /// <summary>Apple Mobile Device Service keeps its pair records here on Windows.</summary>
        private static Dictionary<string, object?>? TryReadFromDisk(string udid)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Apple", "Lockdown");
                string path   = Path.Combine(folder, udid + ".plist");
                return File.Exists(path) ? PropertyList.ParseDictionary(File.ReadAllBytes(path)) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// lockdownd client (device port 62078). Used to read public identity values and,
    /// for a device that already trusts this PC, to open a TLS session and start the
    /// AFC file service. There is deliberately no Pair request in this class.
    /// </summary>
    public sealed class LockdownClient : IDisposable
    {
        private const int    LockdownPort = 62078;
        private const string Label        = "HashDash";

        private readonly UsbmuxClient _mux;
        private Stream  _stream;
        private string? _sessionId;

        public LockdownClient(long deviceId)
        {
            _mux    = new UsbmuxClient();
            _stream = _mux.Connect(deviceId, LockdownPort);
        }

        /// <summary>Reads a value the device exposes without a session (e.g. DeviceName, ProductVersion).</summary>
        public string? GetValue(string key)
        {
            var reply = Request(new() { ["Request"] = "GetValue", ["Key"] = key });
            return reply.TryGetValue("Value", out var v) ? v?.ToString() : null;
        }

        /// <summary>
        /// Starts an authenticated session using the existing pair record and upgrades the
        /// connection to TLS. The device's certificate is pinned to the one stored at pairing.
        /// </summary>
        public void StartSession(PairRecord pair)
        {
            var reply = Request(new()
            {
                ["Request"]    = "StartSession",
                ["HostID"]     = pair.HostId,
                ["SystemBUID"] = pair.SystemBuid
            });

            ThrowOnError(reply);
            _sessionId = reply.GetString("SessionID");

            if (reply.GetBool("EnableSessionSSL"))
                _stream = Secure(_stream, pair);
        }

        /// <summary>Starts a device service and returns its port and whether it requires TLS.</summary>
        public (int port, bool useTls) StartService(string service)
        {
            var reply = Request(new() { ["Request"] = "StartService", ["Service"] = service });
            ThrowOnError(reply);
            return ((int)(reply.GetInteger("Port") ?? 0), reply.GetBool("EnableServiceSSL"));
        }

        public static Stream Secure(Stream inner, PairRecord pair)
        {
            byte[]? pinned = null;
            if (pair.DeviceCertificatePem.Length > 0)
            {
                try { pinned = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(pair.DeviceCertificatePem)).GetPublicKey(); }
                catch { pinned = null; }
            }

            var ssl = new SslStream(inner, leaveInnerStreamOpen: true, (_, cert, _, _) =>
                pinned == null || (cert != null && cert.GetPublicKey().SequenceEqual(pinned)));

            ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
            {
                TargetHost                     = "",
                ClientCertificates             = new X509CertificateCollection { pair.HostCertificate },
                EnabledSslProtocols            = SslProtocols.None, // let Windows negotiate the best protocol the device supports
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            });
            return ssl;
        }

        private static void ThrowOnError(Dictionary<string, object?> reply)
        {
            string? error = reply.GetString("Error");
            if (error == null) return;

            throw new InvalidOperationException(error switch
            {
                "PasswordProtected" => "The device is locked. Unlock it with its passcode and try again.",
                "InvalidHostID"     => "This PC's pairing with the device is no longer valid (trust was reset on the device).",
                "SessionInactive"   => "The device closed the session. Unlock it and try again.",
                _                   => $"The device refused the request ({error})."
            });
        }

        private Dictionary<string, object?> Request(Dictionary<string, object?> message)
        {
            message["Label"] = Label;
            byte[] body   = PropertyList.ToXml(message);
            byte[] length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
            _stream.Write(length);
            _stream.Write(body);
            _stream.Flush();

            uint replyLength = BinaryPrimitives.ReadUInt32BigEndian(StreamUtil.ReadExactly(_stream, 4));
            if (replyLength > 16 * 1024 * 1024) throw new InvalidDataException("Malformed lockdown reply.");
            return PropertyList.ParseDictionary(StreamUtil.ReadExactly(_stream, (int)replyLength));
        }

        public void Dispose()
        {
            if (_sessionId != null)
            {
                try { Request(new() { ["Request"] = "StopSession", ["SessionID"] = _sessionId }); }
                catch { /* best effort */ }
            }

            if (_stream is SslStream) _stream.Dispose();
            _mux.Dispose();
        }
    }
}
