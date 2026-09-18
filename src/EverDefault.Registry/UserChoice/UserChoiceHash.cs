using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EverDefault.Registry.UserChoice
{
    /// <summary>
    /// Reverse-engineered candidate set for the Windows UserChoice hash.
    ///
    /// Windows stores a Hash value under UserChoice that is validated against the
    /// user SID, the file extension, the ProgId and the key's LastWriteTime. The exact
    /// formula is undocumented, so this class brute-forces a family of formulations
    /// against a known-good sample to discover the one this OS build uses.
    /// </summary>
    public static class UserChoiceHash
    {
        public sealed class Variant
        {
            public string Description { get; set; }
            public string Hash { get; set; }
        }

        public static IList<Variant> BruteForce(
            string expected,
            string extension,
            string sid,
            string progId,
            long fileTime)
        {
            var matches = new List<Variant>();

            var encodings = new List<Tuple<string, Encoding>>
            {
                Tuple.Create<string, Encoding>("utf16le", new UnicodeEncoding(false, false)),
                Tuple.Create<string, Encoding>("utf8", new UTF8Encoding(false)),
                Tuple.Create<string, Encoding>("ascii", Encoding.ASCII)
            };

            var eWithDot = extension.ToLowerInvariant();
            var eNoDot = eWithDot.TrimStart('.');

            var orderings = new[]
            {
                "ESP", "EPS", "SEP", "SPE", "PES", "PSE"
            };

            var separators = new[] { "", "-", "_", "|", " " };

            var timestamps = TimestampFormats(fileTime);

            foreach (var ordering in orderings)
            {
                foreach (var sep in separators)
                {
                    // E placeholder resolves to either ".pdf" or "pdf"
                    foreach (var e in new[] { eWithDot, eNoDot })
                    {
                        var parts = new List<string>();
                        foreach (var c in ordering)
                        {
                            if (c == 'E') parts.Add(e);
                            else if (c == 'S') parts.Add(sid);
                            else parts.Add(progId);
                        }

                        var core = string.Join(sep, parts);
                        var cores = new[] { core, core.ToLowerInvariant() };

                        foreach (var c in cores)
                        {
                            foreach (var ts in timestamps)
                            {
                                var strings = new[]
                                {
                                    Tuple.Create("append", c + sep + ts),
                                    Tuple.Create("prepend", ts + sep + c)
                                };

                                foreach (var s in strings)
                                {
                                    foreach (var enc in encodings)
                                    {
                                        var bytes = enc.Item2.GetBytes(s.Item2);

                                        CheckHash(matches, expected, MD5.Create().ComputeHash(bytes),
                                            string.Format("order={0} sep='{1}' ts={2} enc={3} pos={4} lower={5}",
                                                ordering, sep, ts, enc.Item1, s.Item1, c != core));

                                        var withRaw = Append(bytes, BitConverter.GetBytes(fileTime));
                                        CheckHash(matches, expected, MD5.Create().ComputeHash(withRaw),
                                            string.Format("RAW order={0} sep='{1}' enc={2}", ordering, sep, enc.Item1));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return matches;
        }

        private static IEnumerable<string> TimestampFormats(long ft)
        {
            var list = new List<string>();
            ulong uft = (ulong)ft;

            list.Add(ft.ToString(CultureInfo.InvariantCulture));
            list.Add(uft.ToString(CultureInfo.InvariantCulture));
            list.Add(ft.ToString("X16", CultureInfo.InvariantCulture));
            list.Add(ft.ToString("x16", CultureInfo.InvariantCulture));
            list.Add(((uint)ft).ToString(CultureInfo.InvariantCulture));
            list.Add(((uint)ft).ToString("X8", CultureInfo.InvariantCulture));
            list.Add(((uint)(ft >> 32)).ToString(CultureInfo.InvariantCulture));
            list.Add(((uint)(ft >> 32)).ToString("X8", CultureInfo.InvariantCulture));
            list.Add((((uint)ft) ^ ((uint)(ft >> 32))).ToString(CultureInfo.InvariantCulture));
            list.Add((ft / 10000000L).ToString(CultureInfo.InvariantCulture));
            list.Add(((ft / 10000000L) - 11644473600L).ToString(CultureInfo.InvariantCulture));
            list.Add(((uint)(ft >> 32)).ToString(CultureInfo.InvariantCulture) + ((uint)ft).ToString(CultureInfo.InvariantCulture));

            try
            {
                var dt = DateTime.FromFileTimeUtc(ft);
                list.Add(dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                list.Add(dt.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            return list;
        }

        private static void CheckHash(ICollection<Variant> matches, string expected, byte[] md5, string description)
        {
            foreach (var candidate in Derive(md5))
            {
                if (string.Equals(candidate.Item2, expected, StringComparison.Ordinal))
                {
                    matches.Add(new Variant { Description = description + " derive=" + candidate.Item1, Hash = candidate.Item2 });
                }
            }
        }

        private static IEnumerable<Tuple<string, string>> Derive(byte[] md5)
        {
            yield return Tuple.Create("b64[0:8]", Convert.ToBase64String(md5, 0, 8));
            yield return Tuple.Create("b64[8:8]", Convert.ToBase64String(md5, 8, 8));
            yield return Tuple.Create("b64[4:8]", Convert.ToBase64String(md5, 4, 8));

            var xor = new byte[8];
            for (int i = 0; i < 8; i++)
                xor[i] = (byte)(md5[i] ^ md5[i + 8]);
            yield return Tuple.Create("b64(xor)", Convert.ToBase64String(xor));
        }

        private static byte[] Append(byte[] a, byte[] b)
        {
            var result = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            return result;
        }
    }
}
