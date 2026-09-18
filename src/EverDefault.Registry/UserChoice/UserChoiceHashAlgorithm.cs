using System;
using System.Security.Cryptography;
using System.Text;

namespace EverDefault.Registry.UserChoice
{
    /// <summary>
    /// Port of the reverse-engineered UserChoice hash ("patent hash").
    ///
    /// Input string (lower-cased):
    ///   extension + SID + ProgId + formattedLastWriteTime + trailingString
    /// where formattedLastWriteTime is the key's LastWriteTime truncated to the minute,
    /// rendered as "{high32:x8}{low32:x8}".
    ///
    /// The UTF-16LE bytes (plus a 2-byte null terminator) are MD5'd and then fed into a
    /// custom two-pass mixer (WordSwap + Reversible). The two 32-bit results are XOR'd
    /// and the resulting Int64 is Base64-encoded.
    ///
    /// Reference (public domain / Unlicense):
    ///   https://github.com/default-username-was-already-taken/set-fileassoc
    /// </summary>
    public static class UserChoiceHashAlgorithm
    {
        private const string TrailingString =
            "User Choice set via Windows User Experience {D18B6DD5-6124-4341-9318-804003BAFA0B}";

        public static string Compute(string extension, string sid, string progId, long lastWriteTimeFileTime)
        {
            var writeTime = FormatWriteTime(lastWriteTimeFileTime);
            var input = (extension + sid + progId + writeTime + TrailingString).ToLowerInvariant();

            var bytes = Encoding.Unicode.GetBytes(input);
            var data = new byte[bytes.Length + 2];
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);

            byte[] md5;
            using (var md5Alg = MD5.Create())
                md5 = md5Alg.ComputeHash(data);

            var size = data.Length;
            var shiftedSize = (size >> 2) - ((size >> 2) & 1);

            var a1 = WordSwap(data, shiftedSize, md5);
            var a2 = Reversible(data, shiftedSize, md5);

            var value = MakeLong(a1[1] ^ a2[1], a1[0] ^ a2[0]);
            return Convert.ToBase64String(BitConverter.GetBytes(value));
        }

        public static string FormatWriteTime(long fileTime)
        {
            // Mimic the reference: convert to local, truncate to whole minutes, back to file time.
            var local = DateTime.FromFileTime(fileTime);
            var truncated = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Kind)
                .ToFileTime();

            return string.Format("{0:x8}{1:x8}", (uint)(truncated >> 32), (uint)(truncated & 0xFFFFFFFF));
        }

        private static long MakeLong(uint left, uint right)
        {
            return (long)left << 32 | (long)right;
        }

        private static uint[] WordSwap(byte[] a, int sz, byte[] md5)
        {
            if (sz < 2 || (sz & 1) == 1)
                throw new ArgumentException("Invalid input size: " + sz, "sz");

            unchecked
            {
                uint o1 = 0;
                uint o2 = 0;
                int ta = 0;
                int ts = sz;
                int ti = ((sz - 2) >> 1) + 1;

                uint c0 = (BitConverter.ToUInt32(md5, 0) | 1) + 0x69FB0000;
                uint c1 = (BitConverter.ToUInt32(md5, 4) | 1) + 0x13DB0000;

                for (uint i = (uint)ti; i > 0; i--)
                {
                    uint n = BitConverter.ToUInt32(a, ta) + o1;
                    ta += 8;
                    ts -= 2;

                    uint t1 = n * c0 - 0x10FA9605 * (n >> 16);
                    uint v1 = 0x79F8A395 * t1 + 0x689B6B9F * (t1 >> 16);
                    uint v2 = 0xEA970001 * v1 - 0x3C101569 * (v1 >> 16);
                    uint v3 = BitConverter.ToUInt32(a, ta - 4) + v2;
                    uint v4 = v3 * c1 - 0x3CE8EC25 * (v3 >> 16);
                    uint v5 = 0x59C3AF2D * v4 - 0x2232E0F1 * (v4 >> 16);

                    o1 = 0x1EC90001 * v5 + 0x35BD1EC9 * (v5 >> 16);
                    o2 += o1 + v2;
                }

                if (ts == 1)
                {
                    uint n = BitConverter.ToUInt32(a, ta) + o1;

                    uint v1 = n * c0 - 0x10FA9605 * (n >> 16);
                    uint w1 = 0x79F8A395 * v1 + 0x689B6B9F * (v1 >> 16);
                    uint v2 = 0xEA970001 * w1 - 0x3C101569 * (w1 >> 16);
                    uint v3 = v2 * c1 - 0x3CE8EC25 * (v2 >> 16);
                    uint w2 = 0x59C3AF2D * v3 - 0x2232E0F1 * (v3 >> 16);

                    o1 = 0x1EC90001 * w2 + 0x35BD1EC9 * (w2 >> 16);
                    o2 += o1 + v2;
                }

                return new[] { o1, o2 };
            }
        }

        private static uint[] Reversible(byte[] a, int sz, byte[] md5)
        {
            if (sz < 2 || (sz & 1) == 1)
                throw new ArgumentException("Invalid input size: " + sz, "sz");

            unchecked
            {
                uint o1 = 0;
                uint o2 = 0;
                int ta = 0;
                int ts = sz;
                int ti = ((sz - 2) >> 1) + 1;

                uint c0 = BitConverter.ToUInt32(md5, 0) | 1;
                uint c1 = BitConverter.ToUInt32(md5, 4) | 1;

                for (uint i = (uint)ti; i > 0; i--)
                {
                    uint n = (BitConverter.ToUInt32(a, ta) + o1) * c0;
                    n = 0xB1110000 * n - 0x30674EEF * (n >> 16);
                    ta += 8;
                    ts -= 2;

                    uint v1 = 0x5B9F0000 * n - 0x78F7A461 * (n >> 16);
                    uint t1 = 0x12CEB96D * (v1 >> 16) - 0x46930000 * v1;
                    uint v2 = 0x1D830000 * t1 + 0x257E1D83 * (t1 >> 16);
                    uint v3 = BitConverter.ToUInt32(a, ta - 4) + v2;

                    uint v4 = 0x16F50000 * c1 * v3 - 0x5D8BE90B * ((c1 * v3) >> 16);
                    uint t2 = 0x96FF0000 * v4 - 0x2C7C6901 * (v4 >> 16);
                    uint v5 = 0x2B890000 * t2 + 0x7C932B89 * (t2 >> 16);

                    o1 = 0x9F690000 * v5 - 0x405B6097 * (v5 >> 16);
                    o2 += o1 + v2;
                }

                if (ts == 1)
                {
                    uint n = BitConverter.ToUInt32(a, ta) + o1;

                    uint v1 = 0xB1110000 * c0 * n - 0x30674EEF * ((c0 * n) >> 16);
                    uint v2 = 0x5B9F0000 * v1 - 0x78F7A461 * (v1 >> 16);
                    uint t1 = 0x12CEB96D * (v2 >> 16) - 0x46930000 * v2;
                    uint v3 = 0x1D830000 * t1 + 0x257E1D83 * (t1 >> 16);
                    uint v4 = 0x16F50000 * c1 * v3 - 0x5D8BE90B * ((c1 * v3) >> 16);
                    uint v5 = 0x96FF0000 * v4 - 0x2C7C6901 * (v4 >> 16);
                    uint t2 = 0x2B890000 * v5 + 0x7C932B89 * (v5 >> 16);

                    o1 = 0x9F690000 * t2 - 0x405B6097 * (t2 >> 16);
                    o2 += o1 + v2;
                }

                return new[] { o1, o2 };
            }
        }
    }
}
