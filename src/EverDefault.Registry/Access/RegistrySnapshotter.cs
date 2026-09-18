using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Util;

namespace EverDefault.Registry.Access
{
    /// <summary>Captures and diffs registry value sets for custom-registry rules.</summary>
    public static class RegistrySnapshotter
    {
        public static IReadOnlyList<RegistryValueData> Capture(
            IRegistryAccess access, IEnumerable<string> keyPatterns, bool recursive)
        {
            var results = new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in keyPatterns ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                if (!Wildcard.HasWildcard(pattern))
                {
                    CaptureKey(access, pattern, results);
                    continue;
                }

                var prefix = Wildcard.StaticPrefix(pattern);
                if (string.IsNullOrEmpty(prefix))
                    continue;

                foreach (var sub in access.EnumerateSubKeys(prefix, true))
                {
                    if (Wildcard.IsMatch(sub, pattern))
                        CaptureKey(access, sub, results);
                }
            }

            return results.Values.ToList();
        }

        public static IReadOnlyList<RegistryChange> Diff(
            IEnumerable<RegistryValueData> baseline,
            IEnumerable<RegistryValueData> current,
            ChangeOrigin origin)
        {
            var before = ToMap(baseline);
            var after = ToMap(current);
            var changes = new List<RegistryChange>();

            foreach (var pair in after)
            {
                RegistryValueData old;
                if (!before.TryGetValue(pair.Key, out old)
                    || !string.Equals(old.Data, pair.Value.Data, StringComparison.Ordinal)
                    || !string.Equals(old.Kind, pair.Value.Kind, StringComparison.Ordinal))
                {
                    changes.Add(ToChange(pair.Value, origin));
                }
            }

            foreach (var pair in before)
            {
                if (!after.ContainsKey(pair.Key))
                {
                    changes.Add(new RegistryChange
                    {
                        KeyPath = pair.Value.KeyPath,
                        ValueName = pair.Value.Name,
                        Exists = false,
                        Origin = origin,
                        KeyLastWriteTimeUtc = pair.Value.KeyLastWriteTimeUtc
                    });
                }
            }

            return changes;
        }

        private static void CaptureKey(IRegistryAccess access, string keyPath, IDictionary<string, RegistryValueData> sink)
        {
            foreach (var value in access.EnumerateValues(keyPath))
                sink[keyPath + "\u0000" + value.Name] = value;
        }

        private static Dictionary<string, RegistryValueData> ToMap(IEnumerable<RegistryValueData> values)
        {
            var map = new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values ?? Enumerable.Empty<RegistryValueData>())
                map[value.KeyPath + "\u0000" + value.Name] = value;
            return map;
        }

        private static RegistryChange ToChange(RegistryValueData value, ChangeOrigin origin)
        {
            return new RegistryChange
            {
                KeyPath = value.KeyPath,
                ValueName = value.Name,
                Exists = value.Exists,
                ValueKind = value.Kind,
                Data = value.Data,
                KeyLastWriteTimeUtc = value.KeyLastWriteTimeUtc,
                Origin = origin
            };
        }
    }
}
