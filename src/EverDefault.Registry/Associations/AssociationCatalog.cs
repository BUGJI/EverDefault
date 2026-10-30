using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Registry.Apps;
using Microsoft.Win32;
using Win32Registry = Microsoft.Win32.Registry;

namespace EverDefault.Registry.Associations
{
    public enum AssociationKind
    {
        FileExtension = 0,
        UrlProtocol = 1
    }

    /// <summary>One current default handler read from the user's UserChoice key.</summary>
    public sealed class AssociationEntry
    {
        public AssociationKind Kind { get; set; }

        /// <summary>Lower-case ".pdf" for extensions, "http" for protocols.</summary>
        public string Name { get; set; }

        public string ProgId { get; set; }

        public string AppName { get; set; }

        public string AppPath { get; set; }

        public string FriendlyTarget
        {
            get
            {
                if (Kind == AssociationKind.UrlProtocol)
                {
                    switch (Name)
                    {
                        case "http":
                        case "https":
                            return "网页链接";
                        case "mailto":
                            return "邮件链接";
                        default:
                            return Name + " 链接";
                    }
                }

                return Name;
            }
        }
    }

    /// <summary>All targets currently handled by one application.</summary>
    public sealed class AssociationAppGroup
    {
        public string AppName { get; set; }

        public string AppPath { get; set; }

        public List<AssociationEntry> Entries { get; } = new List<AssociationEntry>();

        public List<string> Extensions { get; } = new List<string>();

        public List<string> Protocols { get; } = new List<string>();

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (Extensions.Count > 0)
                {
                    parts.Add(Extensions.Count <= 6
                        ? string.Join(", ", Extensions)
                        : string.Join(", ", Extensions.Take(6)) + " 等 " + Extensions.Count + " 项");
                }

                if (Protocols.Count > 0)
                    parts.Add(Protocols.Count == 1 ? "1 个链接类型" : Protocols.Count + " 个链接类型");

                return parts.Count == 0 ? "未指定" : string.Join("；", parts);
            }
        }

        public string DisplayName
        {
            get
            {
                var count = Entries.Count;
                return (AppName ?? "未知程序") + "（" + count + " 项）";
            }
        }
    }

    /// <summary>
    /// Reads the current user's effective default handlers. The tray process runs inside the
    /// logged-on user, so HKCU here is exactly the profile the user is looking at.
    /// </summary>
    public static class AssociationCatalog
    {
        private const string FileExtsBase =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

        private const string UrlAssociationsBase =
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations";

        private static readonly Dictionary<string, AppDisplayInfo> Cache =
            new Dictionary<string, AppDisplayInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new object();

        public static IReadOnlyList<AssociationEntry> LoadCurrentUser()
        {
            var entries = new List<AssociationEntry>();

            ReadRoot(FileExtsBase, AssociationKind.FileExtension, entries);
            ReadRoot(UrlAssociationsBase, AssociationKind.UrlProtocol, entries);

            return entries;
        }

        public static AssociationEntry Resolve(string name, AssociationKind kind)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var normalized = kind == AssociationKind.UrlProtocol
                ? NormalizeProtocol(name)
                : NormalizeExtension(name);

            if (normalized == null)
                return null;

            var basePath = kind == AssociationKind.UrlProtocol ? UrlAssociationsBase : FileExtsBase;

            try
            {
                using (var key = Win32Registry.CurrentUser.OpenSubKey(basePath + "\\" + normalized + "\\UserChoice"))
                {
                    var progId = key == null ? null : key.GetValue("ProgId", null) as string;
                    return CreateEntry(kind, normalized, progId);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<AssociationAppGroup> GroupByApp(IEnumerable<AssociationEntry> entries)
        {
            var groups = new Dictionary<string, AssociationAppGroup>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var entry in entries ?? Enumerable.Empty<AssociationEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ProgId))
                    continue;

                var key = (entry.AppName ?? entry.ProgId) + "|" + (entry.AppPath ?? string.Empty);
                AssociationAppGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new AssociationAppGroup
                    {
                        AppName = entry.AppName,
                        AppPath = entry.AppPath
                    };
                    groups[key] = group;
                    order.Add(key);
                }

                if (!group.Entries.Any(e => e.Kind == entry.Kind
                        && string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    group.Entries.Add(entry);

                    if (entry.Kind == AssociationKind.UrlProtocol)
                    {
                        if (!group.Protocols.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                            group.Protocols.Add(entry.Name);
                    }
                    else
                    {
                        if (!group.Extensions.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                            group.Extensions.Add(entry.Name);
                    }
                }
            }

            return order.Select(key => groups[key])
                .OrderBy(g => g.AppName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return null;

            var value = extension.Trim().ToLowerInvariant();
            if (value.Length == 0)
                return null;

            return value[0] == '.' ? value : "." + value;
        }

        public static string NormalizeProtocol(string protocol)
        {
            if (string.IsNullOrWhiteSpace(protocol))
                return null;

            var value = protocol.Trim().TrimEnd(':').Trim().ToLowerInvariant();
            return value.Length == 0 ? null : value;
        }

        private static void ReadRoot(string basePath, AssociationKind kind, List<AssociationEntry> output)
        {
            try
            {
                using (var root = Win32Registry.CurrentUser.OpenSubKey(basePath))
                {
                    if (root == null)
                        return;

                    foreach (var name in root.GetSubKeyNames())
                    {
                        if (name.StartsWith(".bak", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string progId;
                        try
                        {
                            using (var choice = root.OpenSubKey(name + "\\UserChoice"))
                                progId = choice == null ? null : choice.GetValue("ProgId", null) as string;
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        var entry = CreateEntry(kind, kind == AssociationKind.UrlProtocol
                            ? NormalizeProtocol(name)
                            : NormalizeExtension(name), progId);

                        if (entry != null)
                            output.Add(entry);
                    }
                }
            }
            catch (Exception)
            {
                // A locked or missing root simply yields no entries.
            }
        }

        private static AssociationEntry CreateEntry(AssociationKind kind, string name, string progId)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(progId))
                return null;

            var trimmed = progId.Trim();
            var app = ResolveApp(trimmed);
            var appName = app != null ? app.Name : null;
            var packaged = trimmed.StartsWith("AppX", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(appName) || packaged || appName.Length > 42)
            {
                appName = kind == AssociationKind.UrlProtocol
                    ? ProtocolAppLabel(name)
                    : "系统应用";
            }

            return new AssociationEntry
            {
                Kind = kind,
                Name = name,
                ProgId = trimmed,
                AppName = appName,
                AppPath = app != null ? app.ExePath : null
            };
        }

        private static string ProtocolAppLabel(string protocol)
        {
            switch (protocol)
            {
                case "http":
                case "https":
                    return "默认浏览器";
                case "mailto":
                    return "邮件应用";
                case "bingmaps":
                    return "地图应用";
                case "mswindowsmusic":
                    return "音乐应用";
                case "mswindowsvideo":
                    return "视频应用";
                case "ms-photos":
                    return "照片应用";
                default:
                    return protocol + " 应用";
            }
        }

        private static AppDisplayInfo ResolveApp(string progId)
        {
            lock (Sync)
            {
                AppDisplayInfo cached;
                if (Cache.TryGetValue(progId, out cached))
                    return cached;

                var resolved = AppDisplayResolver.FromProgId(progId);
                Cache[progId] = resolved;
                return resolved;
            }
        }
    }
}
