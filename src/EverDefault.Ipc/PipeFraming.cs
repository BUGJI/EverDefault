using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace EverDefault.Ipc
{
    /// <summary>Length-prefixed (Int32 LE) UTF-8 JSON framing for pipe streams.</summary>
    internal static class PipeFraming
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public static void Write(Stream stream, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json ?? string.Empty);
            var length = BitConverter.GetBytes(payload.Length);
            stream.Write(length, 0, 4);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        public static string Read(Stream stream)
        {
            var header = ReadExactly(stream, 4);
            if (header == null)
                return null;

            var length = BitConverter.ToInt32(header, 0);
            if (length <= 0)
                return string.Empty;

            var payload = ReadExactly(stream, length);
            return payload == null ? null : Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;

            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    return offset == 0 ? null : buffer;
                offset += read;
            }

            return buffer;
        }
    }
}
