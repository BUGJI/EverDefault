using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>Builds short, readable rule names from the configured fields.</summary>
    public static class RuleNaming
    {
        public static string Suggest(RuleBase rule)
        {
            var app = rule as DefaultAppRule;
            if (app != null)
                return ForDefaultApp(app.Extensions, app.ProgId, app.AppName, app.Protocols);

            var nameSpace = rule as NameSpaceRule;
            if (nameSpace != null)
                return ForNameSpace(nameSpace.MatchPattern);

            var custom = rule as CustomRegistryRule;
            if (custom != null)
                return ForCustom(custom.KeyPatterns, custom.ValueName);

            return "规则";
        }

        public static string ForDefaultApp(IList<string> extensions, string progId)
        {
            return ForDefaultApp(extensions, progId, null, null);
        }

        public static string ForDefaultApp(IList<string> extensions, string progId, string appName)
        {
            return ForDefaultApp(extensions, progId, appName, null);
        }

        public static string ForDefaultApp(
            IList<string> extensions, string progId, string appName, IList<string> protocols)
        {
            var exts = Clean(extensions, true);
            var protos = Clean(protocols, true);
            var prog = Trim(progId);
            var app = Trim(appName);

            var label = BuildTargetLabel(exts, protos);
            if (label == null)
                return prog == null ? "默认应用规则" : "锁定默认程序 " + prog;

            if (app != null)
                return label + " → " + app;

            return prog == null ? label + " 默认程序" : label + " → " + prog;
        }

        private static string BuildTargetLabel(List<string> extensions, List<string> protocols)
        {
            var all = new List<string>(extensions);
            all.AddRange(protocols);
            if (all.Count == 0)
                return null;

            return all.Count == 1 ? all[0] : all[0] + " 等 " + all.Count + " 项";
        }

        public static string ForNameSpace(string matchPattern)
        {
            var pattern = Trim(matchPattern);
            return pattern == null ? "命名空间清理" : "命名空间 " + Clip(pattern, 30);
        }

        public static string ForCustom(IList<string> keyPatterns, string valueName)
        {
            var keys = Clean(keyPatterns, false);
            if (keys.Count == 0)
                return "自定义注册表规则";

            var value = string.IsNullOrEmpty(valueName) ? "(默认)" : valueName.Trim();
            var name = Leaf(keys[0]) + "\\" + value;
            if (keys.Count > 1)
                name += " 等 " + keys.Count + " 项";

            return Clip(name, 48);
        }

        private static string Leaf(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            var text = key.Trim().TrimEnd('\\');
            if (text.EndsWith("*"))
            {
                text = text.Substring(0, text.Length - 1).TrimEnd('\\');
            }

            var slash = text.LastIndexOf('\\');
            return slash < 0 ? text : text.Substring(slash + 1);
        }

        private static List<string> Clean(IList<string> values, bool lowerCase)
        {
            var result = new List<string>();
            if (values == null)
                return result;

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var text = value.Trim();
                if (lowerCase)
                    text = text.ToLowerInvariant();

                if (!result.Contains(text))
                    result.Add(text);
            }

            return result;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Clip(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value;

            return value.Substring(0, max - 1) + "…";
        }
    }
}
