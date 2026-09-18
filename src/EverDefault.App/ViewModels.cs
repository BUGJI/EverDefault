using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public sealed class RuleRow
    {
        public RuleRow(RuleBase rule)
        {
            Rule = rule;
        }

        public RuleBase Rule { get; private set; }

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
                return string.Join("; ", custom.KeyPatterns) + " -> " + (custom.ExpectedData ?? "(基线)");

            var nameSpace = rule as NameSpaceRule;
            if (nameSpace != null)
                return Display.NsMatch(nameSpace.MatchType) + " = " + nameSpace.MatchPattern;

            var app = rule as DefaultAppRule;
            if (app != null)
                return string.Join(",", app.Extensions) + " -> " + app.ProgId;

            return string.Empty;
        }
    }

    public sealed class LogRow
    {
        public LogRow(ChangeLogEntry entry, IDictionary<Guid, string> ruleNames)
        {
            Time = entry.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss");
            Rule = ResolveRuleName(entry, ruleNames);
            Key = entry.KeyPath;
            Value = entry.ValueName;
            Action = Display.LogAction(entry.Action);
            Result = Display.LogResult(entry.Result);
        }

        private static string ResolveRuleName(ChangeLogEntry entry, IDictionary<Guid, string> ruleNames)
        {
            if (!entry.RuleId.HasValue)
                return "(系统)";

            string name;
            if (ruleNames != null && ruleNames.TryGetValue(entry.RuleId.Value, out name))
                return name;

            return "(已删除 " + entry.RuleId.Value.ToString().Substring(0, 8) + ")";
        }

        public string Time { get; private set; }

        public string Rule { get; private set; }

        public string Key { get; private set; }

        public string Value { get; private set; }

        public string Action { get; private set; }

        public string Result { get; private set; }
    }
}
