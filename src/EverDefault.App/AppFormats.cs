using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace EverDefault.App
{
    /// <summary>An installed application that has registered file associations.</summary>
    public sealed class AppInfo
    {
        public string DisplayName { get; set; }

        public string ExeName { get; set; }

        public string ExePath { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>File extensions that share one ProgId for a given application.</summary>
    public sealed class FormatGroup
    {
        public string ProgId { get; set; }

        public List<string> Extensions { get; set; } = new List<string>();

        public string ExtensionsText
        {
            get
            {
                if (Extensions == null || Extensions.Count == 0)
                    return string.Empty;

                if (Extensions.Count <= 20)
                    return string.Join(", ", Extensions);

                return string.Join(", ", Extensions.Take(20)) + " 等 " + Extensions.Count + " 项";
            }
        }
    }

    /// <summary>Reads HKEY_CLASSES_ROOT to discover which formats an application handles.</summary>
    public static class AppFormats
    {
        public static List<AppInfo> ListApplications()
        {
            var list = new List<AppInfo>();

            try
            {
                using (var apps = Registry.ClassesRoot.OpenSubKey("Applications"))
                {
                    if (apps != null)
                    {
                        foreach (var name in apps.GetSubKeyNames())
                        {
                            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                continue;

                            string friendly = null;
                            string exePath = null;
                            try
                            {
                                using (var key = apps.OpenSubKey(name))
                                {
                                    if (key != null)
                                    {
                                        friendly = key.GetValue("FriendlyAppName") as string;
                                        exePath = ResolveAppExe(key, name);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }

                            if (string.IsNullOrWhiteSpace(friendly) || friendly.StartsWith("@"))
                                friendly = name;

                            list.Add(new AppInfo
                            {
                                DisplayName = friendly,
                                ExeName = name,
                                ExePath = exePath
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        /// <summary>
        /// Returns the extensions the application can open, grouped by ProgId.
        /// A group maps to one DefaultAppRule because a rule enforces a single ProgId.
        /// </summary>
        public static List<FormatGroup> ScanFormats(string exeName, string exePath)
        {
            var target = string.IsNullOrEmpty(exePath) ? exeName : Normalize(exePath);
            var groups = new Dictionary<string, FormatGroup>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var exeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var root = Registry.ClassesRoot;
            string[] subKeys;
            try
            {
                subKeys = root.GetSubKeyNames();
            }
            catch (Exception)
            {
                return new List<FormatGroup>();
            }

            foreach (var subKey in subKeys)
            {
                if (!subKey.StartsWith("."))
                    continue;

                string defaultProgId = null;
                var candidates = new List<string>();
                try
                {
                    using (var extKey = root.OpenSubKey(subKey))
                    {
                        if (extKey == null)
                            continue;

                        defaultProgId = extKey.GetValue(null) as string;
                        using (var openWith = extKey.OpenSubKey("OpenWithProgids"))
                        {
                            if (openWith != null)
                            {
                                foreach (var name in openWith.GetValueNames())
                                {
                                    if (!string.IsNullOrEmpty(name) && name[0] != '.')
                                        candidates.Add(name);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                string chosen = null;
                if (!string.IsNullOrEmpty(defaultProgId) && MatchesExe(root, defaultProgId, target, exeCache))
                {
                    chosen = defaultProgId;
                }
                else
                {
                    foreach (var candidate in candidates)
                    {
                        if (MatchesExe(root, candidate, target, exeCache))
                        {
                            chosen = candidate;
                            break;
                        }
                    }
                }

                if (chosen != null)
                    Add(groups, order, chosen, subKey.ToLowerInvariant());
            }

            return order.Select(progId => groups[progId]).ToList();
        }

        private static void Add(Dictionary<string, FormatGroup> groups, List<string> order, string progId, string extension)
        {
            FormatGroup group;
            if (!groups.TryGetValue(progId, out group))
            {
                group = new FormatGroup { ProgId = progId };
                groups[progId] = group;
                order.Add(progId);
            }

            if (!group.Extensions.Contains(extension))
                group.Extensions.Add(extension);
        }

        private static bool MatchesExe(RegistryKey root, string progId, string target, Dictionary<string, string> cache)
        {
            string exe;
            if (!cache.TryGetValue(progId, out exe))
            {
                exe = ResolveProgIdExe(root, progId);
                cache[progId] = exe;
            }

            if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(target))
                return false;

            var normalized = Normalize(exe);
            if (normalized == null)
                return false;

            if (string.Equals(normalized, target, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(FileName(normalized), FileName(target), StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveProgIdExe(RegistryKey root, string progId)
        {
            try
            {
                using (var command = root.OpenSubKey(progId + @"\shell\open\command"))
                {
                    return command == null ? null : ExtractExe(command.GetValue(null) as string);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ResolveAppExe(RegistryKey appKey, string exeName)
        {
            try
            {
                using (var command = appKey.OpenSubKey(@"shell\open\command"))
                {
                    var exe = command == null ? null : ExtractExe(command.GetValue(null) as string);
                    if (exe != null)
                        return exe;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                using (var appPath = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName))
                {
                    var value = appPath == null ? null : appPath.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim().Trim('"');
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        public static string ExtractExe(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            string text;
            try
            {
                text = Environment.ExpandEnvironmentVariables(command).Trim();
            }
            catch (Exception)
            {
                text = command.Trim();
            }

            if (text.Length == 0)
                return null;

            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                if (end > 1)
                    return text.Substring(1, end - 1);
            }

            var space = text.IndexOf(' ');
            return space < 0 ? text : text.Substring(0, space);
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var text = path.Trim().Trim('"');
            try
            {
                text = Environment.ExpandEnvironmentVariables(text);
            }
            catch (Exception)
            {
            }

            try
            {
                text = Path.GetFullPath(text);
            }
            catch (Exception)
            {
            }

            return text.TrimEnd('\\');
        }

        private static string FileName(string path)
        {
            try
            {
                return Path.GetFileName(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
