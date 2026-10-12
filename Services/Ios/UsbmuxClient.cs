using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HashDash.Services.Ios
{
    public class UsbmuxDevice
    {
        public long   DeviceId { get; init; }
        public string Udid     { get; init; } = "";
    }

    /// <summary>
    /// Client for Apple Mobile Device Service's usbmux daemon (127.0.0.1:27015 on Windows).
    /// usbmux multiplexes TCP-style connections to services on USB-attached iOS devices
    /// and holds the pairing records Windows has with each device.
    ///
    /// Each instance is one socket; after Connect() succeeds the socket stops speaking
    /// usbmux and becomes a raw tunnel to the requested port on the device.
    /// </summary>
    public sealed class UsbmuxClient : IDisposable
    {
        private const int  Port           = 27015;
        private const uint ProtocolPlist  = 1;
        private const uint MessagePlist   = 8;

        private readonly TcpClient     _tcp;
        private readonly NetworkStream _stream;
        private uint _tag;

        public UsbmuxClient()
        {
            _tcp = new TcpClient { NoDelay = true };
            try
            {
                _tcp.Connect("127.0.0.1", Port);
            }
            catch (SocketException ex)
            {
                _tcp.Dispose();
                throw new InvalidOperationException(
                    "Apple Mobile Device Service is not running. Install Apple Devices (or iTunes) and make sure the service is started.", ex);
            }
            _stream = _tcp.GetStream();
        }

        public static List<UsbmuxDevice> ListUsbDevices()
        {
            using var client = new UsbmuxClient();
            var reply = client.Request(new() { ["MessageType"] = "ListDevices" });

            var devices = new List<UsbmuxDevice>();
            if (reply.TryGetValue("DeviceList", out var list) && list is List<object?> items)
            {
                foreach (var item in items)
                {
                    if (item is not Dictionary<string, object?> entry) continue;
                    var props = entry.GetDict("Properties");
                    if (props == null || props.GetString("ConnectionType") != "USB") continue;

                    devices.Add(new UsbmuxDevice
                    {
                        DeviceId = props.GetInteger("DeviceID") ?? entry.GetInteger("DeviceID") ?? 0,
                        Udid     = NormaliseUdid(props.GetString("SerialNumber") ?? "")
                    });
                }
            }
            return devices;
        }

        /// <summary>Returns the raw pair record, or null when this PC has never been trusted by the device.</summary>
        public static Dictionary<string, object?>? ReadPairRecord(string udid)
        {
            using var client = new UsbmuxClient();
            var reply = client.Request(new()
            {
                ["MessageType"]  = "ReadPairRecord",
                ["PairRecordID"] = udid
            });

            var data = reply.GetData("PairRecordData");
            return data == null ? null : PropertyList.ParseDictionary(data);
        }

        /// <summary>Turns this socket into a tunnel to the given TCP port on the device.</summary>
        public Stream Connect(long deviceId, int port)
        {
            var reply = Request(new()
            {
                ["MessageType"] = "Connect",
                ["DeviceID"]    = deviceId,
                ["PortNumber"]  = (long)(ushort)((port << 8) | (port >> 8)) // network byte order
            });

            long result = reply.GetInteger("Number") ?? -1;
            if (result != 0)
                throw new IOException(result == 3
                    ? $"The device refused a connection to service port {port}."
                    : $"usbmux connect failed (code {result}).");

            return _stream;
        }

        private Dictionary<string, object?> Request(Dictionary<string, object?> message)
        {
            message["ClientVersionString"] = "HashDash";
            message["ProgName"]            = "HashDash";
            message["kLibUSBMuxVersion"]   = 3L;

            byte[] body   = PropertyList.ToXml(message);
            byte[] header = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0),  (uint)(16 + body.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4),  ProtocolPlist);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8),  MessagePlist);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), ++_tag);
            _stream.Write(header);
            _stream.Write(body);

            byte[] replyHeader = StreamUtil.ReadExactly(_stream, 16);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(replyHeader);
            if (length < 16 || length > 16 * 1024 * 1024)
                throw new InvalidDataException("Malformed usbmux reply.");

            return PropertyList.ParseDictionary(StreamUtil.ReadExactly(_stream, (int)length - 16));
        }

        /// <summary>usbmux reports 24-character UDIDs (A12+ devices) without their dash; pair records use the dashed form.</summary>
        private static string NormaliseUdid(string serial) =>
            Regex.IsMatch(serial, "^[0-9A-Fa-f]{24}$") ? serial.Insert(8, "-") : serial;

        public void Dispose()
        {
            _stream.Dispose();
            _tcp.Dispose();
        }
    }

    internal static class StreamUtil
    {
        public static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            stream.ReadExactly(buffer, 0, count);
            return buffer;
        }
    }
}
