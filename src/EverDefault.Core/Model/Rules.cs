using System;
using System.Collections.Generic;

namespace EverDefault.Core.Model
{
    public abstract class RuleBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public bool Enabled { get; set; } = true;

        public RuleMode Mode { get; set; } = RuleMode.Monitor;

        /// <summary>Polling interval for <see cref="RuleMode.Schedule"/>.</summary>
        public int IntervalSeconds { get; set; } = 60;

        public RuleAction Action { get; set; } = RuleAction.Restore;

        public int Priority { get; set; }

        public OsFamily MinOs { get; set; } = OsFamily.Win7;

        public OsFamily MaxOs { get; set; } = OsFamily.Win11;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public abstract RuleModule Module { get; }

        public bool Supports(OsFamily family)
        {
            if (family == OsFamily.Unknown)
                return true;

            return family >= MinOs && family <= MaxOs;
        }
    }

    public sealed class DefaultAppRule : RuleBase
    {
        public override RuleModule Module
        {
            get { return RuleModule.DefaultApp; }
        }

        public List<string> Extensions { get; set; } = new List<string>();

        /// <summary>Single ProgId applied to every extension (apps that register one shared ProgId).</summary>
        public string ProgId { get; set; }

        /// <summary>
        /// Per-extension ProgId (lower-case ".ext" -> ProgId). Used by apps that register a
        /// distinct ProgId for each extension (e.g. VLC.mp4, VLC.mkv). Takes precedence over
        /// <see cref="ProgId"/> when it contains an entry for the extension.
        /// </summary>
        public Dictionary<string, string> ProgIdMap { get; set; } = new Dictionary<string, string>();

        /// <summary>Display name of the target application this group locks (e.g. "VLC").</summary>
        public string AppName { get; set; }

        /// <summary>Resolved exe path of the target application, when known.</summary>
        public string AppPath { get; set; }

        public bool ManageOpenWith { get; set; } = true;

        public bool ManageFileAssociation { get; set; } = true;

        /// <summary>True when at least one ProgId (shared or per-extension) can be resolved.</summary>
        public bool HasDesiredProgId()
        {
            if (!string.IsNullOrWhiteSpace(ProgId))
                return true;

            if (ProgIdMap != null)
            {
                foreach (var value in ProgIdMap.Values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Returns the ProgId to enforce for one extension, or null if none is configured.</summary>
        public string ResolveProgId(string extension)
        {
            var ext = NormalizeExtension(extension);
            if (ProgIdMap != null && ProgIdMap.Count > 0 && ext != null)
            {
                string perExtension;
                if (ProgIdMap.TryGetValue(ext, out perExtension) && !string.IsNullOrWhiteSpace(perExtension))
                    return perExtension.Trim();
            }

            return string.IsNullOrWhiteSpace(ProgId) ? null : ProgId.Trim();
        }

        public string GetAppKey()
        {
            if (!string.IsNullOrWhiteSpace(AppName))
                return AppName.Trim();

            var progId = ProgId;
            if (string.IsNullOrWhiteSpace(progId))
                return null;

            progId = progId.Trim();

            var dot = progId.LastIndexOf('.');
            if (dot > 0)
            {
                var suffix = progId.Substring(dot + 1);
                foreach (var raw in Extensions ?? new List<string>())
                {
                    var ext = NormalizeExtension(raw);
                    if (ext != null && string.Equals(ext.Substring(1), suffix, StringComparison.OrdinalIgnoreCase))
                        return progId.Substring(0, dot);
                }
            }

            return progId;
        }

        /// <summary>Normalizes an extension to lower-case, dot-prefixed form (".pdf").</summary>
        public static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return null;

            var ext = extension.Trim().ToLowerInvariant();
            return ext[0] == '.' ? ext : "." + ext;
        }
    }

    public sealed class NameSpaceRule : RuleBase
    {
        public override RuleModule Module
        {
            get { return RuleModule.NameSpace; }
        }

        public List<string> PathPatterns { get; set; } = new List<string>();

        public NameSpaceMatchType MatchType { get; set; } = NameSpaceMatchType.Guid;

        public string MatchPattern { get; set; }

        public NameSpaceDeleteScope DeleteScope { get; set; } = NameSpaceDeleteScope.NameSpaceOnly;

        public bool RefreshShell { get; set; }
    }

    public sealed class CustomRegistryRule : RuleBase
    {
        public override RuleModule Module
        {
            get { return RuleModule.CustomRegistry; }
        }

        public List<string> KeyPatterns { get; set; } = new List<string>();

        /// <summary>Empty string targets the key's default value.</summary>
        public string ValueName { get; set; } = string.Empty;

        public ValueMatchType MatchType { get; set; } = ValueMatchType.AnyChange;

        public string ExpectedKind { get; set; }

        public string ExpectedData { get; set; }

        public RuleAction OnMismatch { get; set; } = RuleAction.Restore;
    }
}
