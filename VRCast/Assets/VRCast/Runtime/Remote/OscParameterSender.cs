using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VRCast.Remote
{
    /// <summary>
    /// Sends standard VRChat OSC avatar-parameter messages to a locally configured OSC endpoint.
    /// This is an avatar-parameter bridge only; it does not replace VMC skeletal tracking or VRChat FBT.
    /// </summary>
    public sealed class OscParameterSender : IDisposable
    {
        public const string DefaultAddress = "127.0.0.1";
        public const int DefaultPort = 9000;
        private UdpClient _client;

        public string Address { get; private set; } = DefaultAddress;
        public int Port { get; private set; } = DefaultPort;
        public string Status { get; private set; } = "Disabled";
        public bool IsEnabled => _client != null;

        public bool Start(string address = DefaultAddress, int port = DefaultPort)
        {
            Stop();

            if (string.IsNullOrWhiteSpace(address) || port < 1 || port > 65535)
            {
                Status = "Invalid OSC endpoint";
                return false;
            }

            if (!IPAddress.TryParse(address.Trim(), out IPAddress ip)
                || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                Status = "Address must be an IPv4 address";
                return false;
            }

            try
            {
                Address = address.Trim();
                Port = port;
                _client = new UdpClient();
                _client.Connect(new IPEndPoint(ip, port));
                Status = "Ready to send to " + Address + ":" + Port;
                return true;
            }
            catch (SocketException ex)
            {
                Stop();
                Status = "OSC connection failed: " + ex.Message;
                return false;
            }
            catch (ArgumentException ex)
            {
                Stop();
                Status = "Invalid OSC endpoint: " + ex.Message;
                return false;
            }
            catch (ObjectDisposedException)
            {
                Stop();
                Status = "OSC endpoint was disposed";
                return false;
            }
        }

        public bool SendFloat(string parameterName, float value)
        {
            if (!IsEnabled)
            {
                Status = "Disabled";
                return false;
            }

            parameterName = parameterName?.Trim();
            if (!IsValidParameterName(parameterName))
            {
                Status = "Invalid parameter name";
                return false;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Status = "Ignored non-finite parameter value";
                return false;
            }

            return Send(OscParameterPacket.CreateFloat("/avatar/parameters/" + parameterName, value));
        }

        public bool SendInt(string parameterName, int value)
        {
            if (!IsEnabled)
            {
                Status = "Disabled";
                return false;
            }

            parameterName = parameterName?.Trim();
            if (!IsValidParameterName(parameterName))
            {
                Status = "Invalid parameter name";
                return false;
            }

            return Send(OscParameterPacket.CreateInt("/avatar/parameters/" + parameterName, value));
        }

        public bool SendBool(string parameterName, bool value)
        {
            if (!IsEnabled)
            {
                Status = "Disabled";
                return false;
            }

            parameterName = parameterName?.Trim();
            if (!IsValidParameterName(parameterName))
            {
                Status = "Invalid parameter name";
                return false;
            }

            return Send(OscParameterPacket.CreateBool("/avatar/parameters/" + parameterName, value));
        }

        private static bool IsValidParameterName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                // Keep the parameter as one unescaped OSC path segment.
                if (c == '/' || c == '\\' || char.IsControl(c))
                    return false;
            }

            return true;
        }

        private bool Send(byte[] packet)
        {
            try
            {
                _client.Send(packet, packet.Length);
                Status = "Ready to send to " + Address + ":" + Port;
                return true;
            }
            catch (SocketException ex)
            {
                Status = "OSC send failed: " + ex.Message;
                return false;
            }
            catch (ObjectDisposedException)
            {
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (_client != null)
            {
                _client.Close();
                _client = null;
            }

            Status = "Disabled";
        }

        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// Minimal OSC 1.0 encoder for VRChat avatar parameter messages.
    /// OSC strings are UTF-8 and padded to a four-byte boundary; numeric values are big-endian.
    /// </summary>
    public static class OscParameterPacket
    {
        public static byte[] CreateFloat(string address, float value)
        {
            byte[] addressBytes = EncodeString(address);
            byte[] tags = EncodeString(",f");
            byte[] packet = new byte[addressBytes.Length + tags.Length + 4];
            Buffer.BlockCopy(addressBytes, 0, packet, 0, addressBytes.Length);
            Buffer.BlockCopy(tags, 0, packet, addressBytes.Length, tags.Length);
            WriteInt32BigEndian(packet, addressBytes.Length + tags.Length,
                BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
            return packet;
        }

        public static byte[] CreateInt(string address, int value)
        {
            byte[] addressBytes = EncodeString(address);
            byte[] tags = EncodeString(",i");
            byte[] packet = new byte[addressBytes.Length + tags.Length + 4];
            Buffer.BlockCopy(addressBytes, 0, packet, 0, addressBytes.Length);
            Buffer.BlockCopy(tags, 0, packet, addressBytes.Length, tags.Length);
            WriteInt32BigEndian(packet, addressBytes.Length + tags.Length, value);
            return packet;
        }

        public static byte[] CreateBool(string address, bool value)
        {
            byte[] addressBytes = EncodeString(address);
            byte[] tags = EncodeString(value ? ",T" : ",F");
            byte[] packet = new byte[addressBytes.Length + tags.Length];
            Buffer.BlockCopy(addressBytes, 0, packet, 0, addressBytes.Length);
            Buffer.BlockCopy(tags, 0, packet, addressBytes.Length, tags.Length);
            return packet;
        }

        private static byte[] EncodeString(string value)
        {
            byte[] text = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int length = text.Length + 1;
            int paddedLength = (length + 3) & ~3;
            byte[] result = new byte[paddedLength];
            Buffer.BlockCopy(text, 0, result, 0, text.Length);
            return result;
        }

        private static void WriteInt32BigEndian(byte[] destination, int offset, int value)
        {
            destination[offset] = (byte)((value >> 24) & 0xff);
            destination[offset + 1] = (byte)((value >> 16) & 0xff);
            destination[offset + 2] = (byte)((value >> 8) & 0xff);
            destination[offset + 3] = (byte)(value & 0xff);
        }
    }
}
