using System;
using System.Collections.Generic;
using EverDefault.Core.Model;

namespace EverDefault.Core.Engine
{
    /// <summary>
    /// One-time upgrade for rules created before grouped default-apps existed: rules that
    /// lock several extensions of the same application (one extension per rule, each with
    /// its own ProgId) are merged into a single <see cref="DefaultAppRule"/> that carries a
    /// per-extension <see cref="DefaultAppRule.ProgIdMap"/>.
    /// The operation is idempotent: grouped rules are skipped.
    /// </summary>
    public static class DefaultAppRuleGrouper
    {
        /// <summary>Merges legacy per-extension rules in place. Returns true when anything changed.</summary>
        public static bool Upgrade(IList<RuleBase> rules)
        {
            if (rules == null)
                return false;

            var groups = new Dictionary<string, List<DefaultAppRule>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var rule in rules)
            {
                var app = rule as DefaultAppRule;
                if (app == null)
                    continue;

                if (app.ProgIdMap != null && app.ProgIdMap.Count > 0)
                    continue; // already grouped

                var key = app.GetAppKey();
                if (key == null)
                    continue;

                List<DefaultAppRule> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<DefaultAppRule>();
                    groups[key] = list;
                    order.Add(key);
                }

                list.Add(app);
            }

            var changed = false;
            foreach (var key in order)
            {
                var list = groups[key];
                if (list.Count < 2)
                    continue;

                var primary = list[0];
                var extensions = new List<string>();
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var app in list)
                {
                    foreach (var raw in app.Extensions ?? new List<string>())
                    {
                        var ext = DefaultAppRule.NormalizeExtension(raw);
                        var progId = app.ResolveProgId(raw);
                        if (ext == null || string.IsNullOrEmpty(progId))
                            continue;

                        if (!extensions.Contains(ext))
                            extensions.Add(ext);

                        map[ext] = progId;
                    }
                }

                if (extensions.Count == 0)
                    continue;

                primary.Extensions = extensions;
                primary.ProgIdMap = map;
                primary.ProgId = null;
                if (string.IsNullOrWhiteSpace(primary.AppName))
                    primary.AppName = key;

                primary.Name = BuildName(primary.AppName, extensions);
                primary.UpdatedUtc = DateTime.UtcNow;

                for (var i = 1; i < list.Count; i++)
                    rules.Remove(list[i]);

                changed = true;
            }

            return changed;
        }

        private static string BuildName(string appName, List<string> extensions)
        {
            var label = string.IsNullOrWhiteSpace(appName) ? "默认应用" : appName.Trim();
            if (extensions == null || extensions.Count == 0)
                return label;

            var extLabel = extensions.Count == 1
                ? extensions[0]
                : extensions[0] + " 等 " + extensions.Count + " 项";
            return extLabel + " → " + label;
        }
    }
}
