using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Registry.Apps;

namespace EverDefault.App
{
    public sealed class RuleRow : INotifyPropertyChanged
    {
        private RuleBase _rule;

        public RuleRow(RuleBase rule)
        {
            _rule = rule;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public RuleBase Rule
        {
            get { return _rule; }
        }

        public bool IsEnabled
        {
            get { return _rule.Enabled; }
        }

        public bool IsDisabled
        {
            get { return !_rule.Enabled; }
        }

        public string StatusText
        {
            get { return _rule.Enabled ? "启用" : "停用"; }
        }

        public void Update(RuleBase rule)
        {
            _rule = rule;
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(string.Empty));
        }

        public string Name
        {
            get { return Rule.Name; }
        }

        public string Module
        {
            get { return Display.Module(Rule.Module); }
        }

        public string Mode
        {
            get { return Display.Mode(Rule.Mode); }
        }

        public string Action
        {
            get { return Display.Action(Rule.Action); }
        }

        public string Enabled
        {
            get { return Rule.Enabled ? "是" : "否"; }
        }

        public string Target
        {
            get { return Describe(Rule); }
        }

        public string Summary
        {
            get { return Mode + " · " + Action + " · " + (Rule.Enabled ? "启用" : "停用"); }
        }

        private static string Describe(RuleBase rule)
        {
            var custom = rule as CustomRegistryRule;
            if (custom != null)
            {
                var target = custom.OnMismatch == RuleAction.WritePolicy || custom.Action == RuleAction.WritePolicy
                    ? "Policy: " + (custom.PolicyKey ?? "(未设置)")
                    : (custom.ExpectedData ?? "(基线)");
                return string.Join("; ", custom.KeyPatterns) + " -> " + target;
            }

            var nameSpace = rule as NameSpaceRule;
            if (nameSpace != null)
                return Display.NsMatch(nameSpace.MatchType) + " = " + nameSpace.MatchPattern;

            var app = rule as DefaultAppRule;
            if (app != null)
            {
                var target = !string.IsNullOrWhiteSpace(app.AppName)
                    ? app.AppName
                    : (app.ProgId ?? app.ProtocolProgId ?? "(未设置)");

                var parts = new List<string>();
                if (app.Extensions != null && app.Extensions.Count > 0)
                    parts.Add(string.Join(",", app.Extensions));
                if (app.Protocols != null && app.Protocols.Count > 0)
                    parts.Add(string.Join(",", app.Protocols));

                return (parts.Count > 0 ? string.Join("; ", parts) : "(未设置)") + " -> " + target;
            }

            return string.Empty;
        }
    }

    /// <summary>A human-friendly row for the activity page and tray notifications.</summary>
    public sealed class ActivityItem
    {
        public long Id { get; set; }

        public Guid? RuleId { get; set; }

        public string Time { get; set; }

        public string TimeText { get; set; }

        public string Title { get; set; }

        public string Detail { get; set; }

        public string Glyph { get; set; }

        public bool IsBlocked { get; set; }

        public bool IsFailed { get; set; }

        public bool IsLogged { get; set; }

        public bool CanAdjust { get; set; }

        public string RuleName { get; set; }

        public string RawDetail { get; set; }

        public bool HasRawDetail
        {
            get { return !string.IsNullOrWhiteSpace(RawDetail); }
        }

        public string FilterKey
        {
            get { return IsFailed ? "failed" : IsBlocked ? "blocked" : "logged"; }
        }
    }

    public static class ActivityFactory
    {
        public static List<ActivityItem> Build(
            IEnumerable<ChangeLogEntry> entries, IDictionary<Guid, RuleBase> rules)
        {
            var list = new List<ActivityItem>();
            if (entries == null)
                return list;

            foreach (var entry in entries.OrderByDescending(e => e.Utc))
            {
                RuleBase rule = null;
                if (entry.RuleId.HasValue && rules != null)
                    rules.TryGetValue(entry.RuleId.Value, out rule);

                var result = entry.Result ?? string.Empty;
                var failed = string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase);
                var logged = string.Equals(result, "logged", StringComparison.OrdinalIgnoreCase);
                var blocked = string.Equals(result, "restored", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(result, "deleted", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(result, "removed", StringComparison.OrdinalIgnoreCase);

                var item = new ActivityItem
                {
                    Id = entry.Id,
                    RuleId = entry.RuleId,
                    Time = entry.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss"),
                    TimeText = Relative(entry.Utc),
                    IsBlocked = blocked,
                    IsFailed = failed,
                    IsLogged = logged,
                    CanAdjust = rule != null,
                    RuleName = rule != null ? rule.Name : "(已删除的规则)",
                    Glyph = failed ? "\uE7BA" : blocked ? "\uE73E" : "\uE946"
                };

                FillText(item, entry, rule);
                item.RawDetail = BuildRaw(entry);
                list.Add(item);
            }

            return list;
        }

        private static void FillText(ActivityItem item, ChangeLogEntry entry, RuleBase rule)
        {
            var app = rule as DefaultAppRule;
            if (app != null)
            {
                var target = DescribeTarget(app);
                var attempted = FriendlyProgId(entry.OldData);
                var desired = string.IsNullOrWhiteSpace(app.AppName)
                    ? FriendlyProgId(entry.NewData)
                    : app.AppName;

                if (string.IsNullOrWhiteSpace(desired))
                    desired = "原来的程序";

                if (item.IsFailed)
                {
                    item.Title = "默认程序没能改回";
                    item.Detail = attempted == null
                        ? "有程序修改了「" + target + "」，但没能改回 " + desired + "。"
                        : "有程序把「" + target + "」改成了 " + attempted + "，但没能改回 " + desired + "。";
                    return;
                }

                if (item.IsLogged)
                {
                    item.Title = "检测到默认程序变化（仅记录）";
                    item.Detail = attempted == null
                        ? "「" + target + "」发生了变化，按规则只记录、不改动。"
                        : "「" + target + "」被改成了 " + attempted + "，按规则只记录、不改动。";
                    return;
                }

                item.Title = item.IsBlocked ? "默认程序被修改" : "检测到默认程序变化";
                item.Detail = attempted == null
                    ? "「" + target + "」已被修改，已改回 " + desired + "。"
                    : "「" + target + "」被改成了 " + attempted + "，已改回 " + desired + "。";
                return;
            }

            var nameSpace = rule as NameSpaceRule;
            if (nameSpace != null)
            {
                var label = string.IsNullOrWhiteSpace(entry.ValueName) ? "多余图标" : "「" + entry.ValueName + "」";
                if (item.IsFailed)
                {
                    item.Title = "多余图标没能移除";
                    item.Detail = "尝试移除" + label + "失败，请到活动详情查看原因。";
                    return;
                }

                if (item.IsLogged)
                {
                    item.Title = "检测到多余图标（仅记录）";
                    item.Detail = label + "出现在" + LocationOf(entry.KeyPath) + "，按规则只记录、不移除。";
                    return;
                }

                item.Title = "已移除多余图标";
                item.Detail = label + "已从" + LocationOf(entry.KeyPath) + "移除。";
                return;
            }

            var custom = rule as CustomRegistryRule;
            if (custom != null)
            {
                var label = string.IsNullOrWhiteSpace(rule.Name) ? "一项系统设置" : "「" + rule.Name + "」";
                if (item.IsFailed)
                {
                    item.Title = "设置没能还原";
                    item.Detail = "有程序修改了" + label + "，但没能还原。";
                    return;
                }

                if (item.IsLogged)
                {
                    item.Title = "设置被修改（仅记录）";
                    item.Detail = "有程序修改了" + label + "，按规则只记录、不改动。";
                    return;
                }

                item.Title = "设置被修改";
                item.Detail = "有程序修改了" + label + "，已还原。";
                return;
            }

            item.Title = item.IsFailed ? "操作失败" : "检测到变化";
            item.Detail = string.IsNullOrWhiteSpace(entry.Message)
                ? "注册表项发生了变化。"
                : entry.Message;
        }

        private static string DescribeTarget(DefaultAppRule rule)
        {
            var protocols = (rule.Protocols ?? new List<string>())
                .Select(DefaultAppRule.NormalizeProtocol)
                .Where(p => p != null)
                .ToList();
            var extensions = (rule.Extensions ?? new List<string>())
                .Select(DefaultAppRule.NormalizeExtension)
                .Where(e => e != null)
                .ToList();

            var parts = new List<string>();
            if (protocols.Count > 0)
            {
                var set = new HashSet<string>(protocols, StringComparer.OrdinalIgnoreCase);
                if (set.Contains("http") && set.Contains("https"))
                    parts.Add("默认浏览器");
                else if (set.Contains("mailto"))
                    parts.Add("默认邮件");
                else
                    parts.Add(protocols.Count == 1 ? protocols[0] + " 链接" : "链接类型");
            }

            if (extensions.Count > 0)
            {
                parts.Add(extensions.Count == 1
                    ? extensions[0]
                    : extensions[0] + " 等 " + extensions.Count + " 项");
            }

            return parts.Count == 0 ? "默认应用" : string.Join("、", parts);
        }

        private static string FriendlyProgId(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId))
                return null;

            var app = AppDisplayResolver.FromProgId(progId);
            return app == null || string.IsNullOrWhiteSpace(app.Name) ? progId : app.Name;
        }

        private static string LocationOf(string keyPath)
        {
            var text = keyPath ?? string.Empty;
            return text.IndexOf(@"\Desktop\NameSpace", StringComparison.OrdinalIgnoreCase) >= 0
                ? "桌面"
                : "此电脑";
        }

        private static string Relative(DateTime utc)
        {
            var local = utc.ToLocalTime();
            var now = DateTime.Now;
            var span = now - local;

            if (span.TotalSeconds < 60)
                return "刚刚";
            if (span.TotalMinutes < 60)
                return (int)span.TotalMinutes + " 分钟前";
            if (local.Date == now.Date)
                return "今天 " + local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(-1))
                return "昨天 " + local.ToString("HH:mm");
            return local.ToString("MM-dd HH:mm");
        }

        private static string BuildRaw(ChangeLogEntry entry)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.KeyPath))
                lines.Add(entry.KeyPath);
            if (!string.IsNullOrWhiteSpace(entry.ValueName))
                lines.Add("值：" + entry.ValueName);

            if (!string.IsNullOrWhiteSpace(entry.OldData) || !string.IsNullOrWhiteSpace(entry.NewData))
                lines.Add((entry.OldData ?? "(空)") + "  →  " + (entry.NewData ?? "(空)"));

            if (!string.IsNullOrWhiteSpace(entry.Message))
                lines.Add(entry.Message);

            return string.Join(Environment.NewLine, lines);
        }
    }
}
