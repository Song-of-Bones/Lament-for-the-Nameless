using System;
using System.IO;
using System.Net.Sockets;

namespace Codely.UnityMcp
{
    /// <summary>
    /// Wire framing shared by the editor bridge and the MCP server:
    /// little-endian int32 payload length followed by that many UTF-8 bytes.
    ///
    /// Length-prefixed frames avoid the escaping and partial-read problems of
    /// newline-delimited JSON, and keep the transport independent of any native plugin.
    /// </summary>
    public static class Framing
    {
        public const int MaxFrameBytes = 64 * 1024 * 1024; // 64 MiB, same ceiling as the old bridge

        public static bool TryReadFrame(Stream stream, out byte[] payload)
        {
            payload = null;
            byte[] header = new byte[4];
            if (!ReadExact(stream, header, 4)) return false;

            int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (length <= 0 || length > MaxFrameBytes) return false;

            byte[] body = new byte[length];
            if (!ReadExact(stream, body, length)) return false;

            payload = body;
            return true;
        }

        public static bool TryWriteFrame(Stream stream, byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > MaxFrameBytes) return false;
            try
            {
                byte[] header =
                {
                    (byte)(payload.Length & 0xFF),
                    (byte)((payload.Length >> 8) & 0xFF),
                    (byte)((payload.Length >> 16) & 0xFF),
                    (byte)((payload.Length >> 24) & 0xFF)
                };
                stream.Write(header, 0, 4);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
                return true;
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch (SocketException) { return false; }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read;
                try { read = stream.Read(buffer, offset, count - offset); }
                catch (IOException) { return false; }
                catch (ObjectDisposedException) { return false; }
                catch (SocketException) { return false; }

                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }
    }
}
