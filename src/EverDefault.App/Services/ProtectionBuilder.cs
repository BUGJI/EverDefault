using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Registry.Associations;

namespace EverDefault.App
{
    /// <summary>A friendly, app-centric view over one or more underlying rules.</summary>
    public sealed class ProtectionItem
    {
        public string Title { get; set; }

        public string Subtitle { get; set; }

        public string ModuleText { get; set; }

        public string StatusText { get; set; }

        public string Glyph { get; set; }

        public bool Enabled { get; set; }

        public bool Paused { get; set; }

        public string PauseMenuText
        {
            get { return Paused ? "恢复保护" : "暂停 1 小时"; }
        }

        public string AppPath { get; set; }

        public Guid PrimaryId { get; set; }

        public RuleModule Module { get; set; }

        public List<RuleBase> Rules { get; } = new List<RuleBase>();

        public List<Guid> RuleIds
        {
            get { return Rules.Select(r => r.Id).ToList(); }
        }
    }

    /// <summary>Builds friendly protection items and creates rules from current associations.</summary>
    public static class ProtectionBuilder
    {
        public static List<ProtectionItem> BuildItems(IEnumerable<RuleBase> rules)
        {
            var list = new List<ProtectionItem>();
            var all = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => r != null)
                .ToList();

            var appGroups = new Dictionary<string, ProtectionItem>(StringComparer.OrdinalIgnoreCase);
            var appOrder = new List<string>();

            foreach (var rule in all.OfType<DefaultAppRule>())
            {
                var key = AppKey(rule);
                ProtectionItem item;
                if (!appGroups.TryGetValue(key, out item))
                {
                    item = new ProtectionItem
                    {
                        Title = Blank(rule.AppName) ?? Blank(rule.ProgId) ?? key,
                        ModuleText = "默认应用",
                        Glyph = "\uE8B7",
                        Module = RuleModule.DefaultApp,
                        PrimaryId = rule.Id,
                        AppPath = rule.AppPath
                    };
                    appGroups[key] = item;
                    appOrder.Add(key);
                    list.Add(item);
                }

                item.Rules.Add(rule);
            }

            foreach (var item in appGroups.Values)
                ApplyDefaultAppDetails(item);

            foreach (var rule in all.Where(r => r.Module == RuleModule.NameSpace))
            {
                list.Add(new ProtectionItem
                {
                    Title = string.IsNullOrWhiteSpace(rule.Name) ? "此电脑图标清理" : rule.Name,
                    Subtitle = DescribeNameSpace(rule as NameSpaceRule),
                    ModuleText = "此电脑",
                    Glyph = "\uE8B7",
                    Module = RuleModule.NameSpace,
                    PrimaryId = rule.Id,
                    Rules = { rule }
                });
            }

            foreach (var rule in all.Where(r => r.Module == RuleModule.CustomRegistry))
            {
                list.Add(new ProtectionItem
                {
                    Title = string.IsNullOrWhiteSpace(rule.Name) ? "系统设置保护" : rule.Name,
                    Subtitle = DescribeCustom(rule as CustomRegistryRule),
                    ModuleText = "系统设置",
                    Glyph = "\uE713",
                    Module = RuleModule.CustomRegistry,
                    PrimaryId = rule.Id,
                    Rules = { rule }
                });
            }

            foreach (var item in list)
                ApplyStatus(item);

            return list
                .OrderBy(i => i.Module)
                .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Creates one grouped rule per application from the given associations.</summary>
        public static List<DefaultAppRule> BuildRulesFromAssociations(IEnumerable<AssociationAppGroup> groups)
        {
            var rules = new List<DefaultAppRule>();

            foreach (var group in groups ?? Enumerable.Empty<AssociationAppGroup>())
            {
                if (group == null || group.Entries.Count == 0)
                    continue;

                var extensions = group.Entries
                    .Where(e => e.Kind == AssociationKind.FileExtension)
                    .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var progIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in extensions)
                    progIdMap[entry.Name] = entry.ProgId;

                if (extensions.Count > 0)
                {
                    var extList = extensions.Select(e => e.Name).ToList();
                    rules.Add(new DefaultAppRule
                    {
                        Name = RuleNaming.ForDefaultApp(extList, null, group.AppName),
                        AppName = group.AppName,
                        AppPath = group.AppPath,
                        Extensions = extList,
                        ProgIdMap = progIdMap,
                        ManageOpenWith = true,
                        ManageFileAssociation = true
                    });
                }

                foreach (var protocolGroup in group.Entries
                    .Where(e => e.Kind == AssociationKind.UrlProtocol)
                    .GroupBy(e => e.ProgId, StringComparer.OrdinalIgnoreCase))
                {
                    var names = protocolGroup.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    rules.Add(new DefaultAppRule
                    {
                        Name = RuleNaming.ForDefaultApp(null, null, group.AppName, names),
                        AppName = group.AppName,
                        AppPath = group.AppPath,
                        Protocols = names,
                        ProtocolProgId = protocolGroup.Key,
                        ManageOpenWith = true,
                        ManageFileAssociation = true
                    });
                }
            }

            return rules;
        }

        /// <summary>Creates rules that lock the current handler for the given targets.</summary>
        public static List<DefaultAppRule> BuildCurrentRules(
            IEnumerable<string> extensions, IEnumerable<string> protocols)
        {
            var entries = new List<AssociationEntry>();

            foreach (var raw in extensions ?? Enumerable.Empty<string>())
            {
                var entry = AssociationCatalog.Resolve(raw, AssociationKind.FileExtension);
                if (entry != null)
                    entries.Add(entry);
            }

            foreach (var raw in protocols ?? Enumerable.Empty<string>())
            {
                var entry = AssociationCatalog.Resolve(raw, AssociationKind.UrlProtocol);
                if (entry != null)
                    entries.Add(entry);
            }

            return BuildRulesFromAssociations(AssociationCatalog.GroupByApp(entries));
        }

        public static bool IsCovered(IEnumerable<RuleBase> existing, AssociationEntry entry)
        {
            if (entry == null)
                return false;

            foreach (var app in (existing ?? Enumerable.Empty<RuleBase>()).OfType<DefaultAppRule>())
            {
                if (entry.Kind == AssociationKind.FileExtension
                    && app.Extensions != null
                    && app.Extensions.Any(e => string.Equals(
                        DefaultAppRule.NormalizeExtension(e), entry.Name, StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (entry.Kind == AssociationKind.UrlProtocol
                    && app.Protocols != null
                    && app.Protocols.Any(p => string.Equals(
                        DefaultAppRule.NormalizeProtocol(p), entry.Name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private static void ApplyDefaultAppDetails(ProtectionItem item)
        {
            var extensions = new List<string>();
            var protocols = new List<string>();

            foreach (var rule in item.Rules.OfType<DefaultAppRule>())
            {
                foreach (var raw in rule.Extensions ?? new List<string>())
                {
                    var ext = DefaultAppRule.NormalizeExtension(raw);
                    if (ext != null && !extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        extensions.Add(ext);
                }

                foreach (var raw in rule.Protocols ?? new List<string>())
                {
                    var protocol = DefaultAppRule.NormalizeProtocol(raw);
                    if (protocol != null && !protocols.Contains(protocol, StringComparer.OrdinalIgnoreCase))
                        protocols.Add(protocol);
                }

                if (string.IsNullOrWhiteSpace(item.AppPath))
                    item.AppPath = rule.AppPath;
            }

            if (string.IsNullOrWhiteSpace(item.Title) || item.Title == "默认应用")
                item.Title = "默认应用";

            var parts = new List<string>();
            if (protocols.Count > 0)
                parts.Add(ProtocolLabel(protocols));
            if (extensions.Count > 0)
                parts.Add(extensions.Count <= 5
                    ? string.Join("、", extensions)
                    : string.Join("、", extensions.Take(5)) + " 等 " + extensions.Count + " 项");

            item.Subtitle = parts.Count == 0 ? "未指定保护范围" : "打开 " + string.Join(" 和 ", parts);
        }

        private static void ApplyStatus(ProtectionItem item)
        {
            var now = DateTime.UtcNow;
            item.Enabled = item.Rules.Any(r => r.Enabled);
            item.Paused = item.Rules.Any(r => r.PausedUntilUtc.HasValue && r.PausedUntilUtc.Value > now);

            if (!item.Enabled)
                item.StatusText = "已停用";
            else if (item.Paused)
            {
                var until = item.Rules
                    .Where(r => r.PausedUntilUtc.HasValue && r.PausedUntilUtc.Value > now)
                    .Select(r => r.PausedUntilUtc.Value)
                    .Max()
                    .ToLocalTime();
                item.StatusText = "已暂停，将在 " + until.ToString("HH:mm") + " 恢复";
            }
            else
            {
                item.StatusText = "保护中";
            }
        }

        private static string AppKey(DefaultAppRule rule)
        {
            var app = Blank(rule.AppName);
            if (app != null)
                return app;

            var progId = Blank(rule.ProgId) ?? Blank(rule.ProtocolProgId);
            return progId ?? "默认应用";
        }

        private static string DescribeNameSpace(NameSpaceRule rule)
        {
            if (rule == null)
                return string.Empty;

            if (rule.MatchType == NameSpaceMatchType.Guid)
                return "按 GUID 清理 " + (rule.MatchPattern ?? string.Empty);
            return "清理匹配「" + (rule.MatchPattern ?? string.Empty) + "」的项目";
        }

        private static string DescribeCustom(CustomRegistryRule rule)
        {
            if (rule == null)
                return string.Empty;

            var value = string.IsNullOrEmpty(rule.ValueName) ? "(默认值)" : rule.ValueName;
            var mode = rule.OnMismatch == RuleAction.WritePolicy ? "用系统策略强制"
                : rule.OnMismatch == RuleAction.Delete ? "被修改时删除"
                : "被修改时还原";
            return value + " · " + mode;
        }

        private static string ProtocolLabel(IList<string> protocols)
        {
            if (protocols.Count == 0)
                return string.Empty;

            var set = new HashSet<string>(protocols, StringComparer.OrdinalIgnoreCase);
            if (set.Contains("http") && set.Contains("https"))
                return "网页链接";
            if (set.Contains("mailto"))
                return "邮件链接";

            return protocols.Count == 1 ? protocols[0] + " 链接" : protocols.Count + " 个链接类型";
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
