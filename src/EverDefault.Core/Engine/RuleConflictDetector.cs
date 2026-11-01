using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Util;

namespace EverDefault.Core.Engine
{
    /// <summary>
    /// Heuristic conflict detection: two enabled rules in the same module whose target
    /// spaces overlap but whose desired end-states differ are reported so the UI can ask
    /// the user to resolve them. Conflicting rules are not executed by the engine.
    /// </summary>
    public sealed class RuleConflictDetector
    {
        public IReadOnlyList<ConflictReport> Detect(IEnumerable<RuleBase> rules)
        {
            var reports = new List<ConflictReport>();
            var list = rules.Where(r => r != null && r.Enabled).ToList();

            DetectCustomConflicts(list, reports);
            DetectDefaultAppConflicts(list, reports);

            return reports;
        }

        /// <summary>
        /// Custom rules can only conflict when they target the same value name, so group by
        /// it instead of comparing every pair.
        /// </summary>
        private static void DetectCustomConflicts(
            List<RuleBase> rules, List<ConflictReport> reports)
        {
            var groups = new Dictionary<string, List<CustomRegistryRule>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var custom in rules.OfType<CustomRegistryRule>())
            {
                var key = custom.ValueName ?? string.Empty;
                List<CustomRegistryRule> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<CustomRegistryRule>();
                    groups[key] = group;
                }

                group.Add(custom);
            }

            foreach (var group in groups.Values)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    for (var j = i + 1; j < group.Count; j++)
                    {
                        var report = CompareCustom(group[i], group[j]);
                        if (report != null)
                            reports.Add(report);
                    }
                }
            }
        }

        /// <summary>
        /// Default-app rules can only conflict when they share an extension or protocol, so
        /// index by those targets and compare each overlapping pair once.
        /// </summary>
        private static void DetectDefaultAppConflicts(
            List<RuleBase> rules, List<ConflictReport> reports)
        {
            var apps = rules.OfType<DefaultAppRule>().ToList();
            if (apps.Count < 2)
                return;

            var groups = new Dictionary<string, List<int>>(System.StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < apps.Count; i++)
            {
                foreach (var extension in NormalizeExtensions(apps[i].Extensions))
                    AddToGroup(groups, extension, i);

                foreach (var protocol in NormalizeProtocols(apps[i].Protocols))
                    AddToGroup(groups, protocol, i);
            }

            var seen = new HashSet<long>();
            foreach (var group in groups.Values)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    for (var j = i + 1; j < group.Count; j++)
                    {
                        var first = group[i];
                        var second = group[j];
                        if (first > second)
                        {
                            var swap = first;
                            first = second;
                            second = swap;
                        }

                        var key = ((long)first << 32) | (uint)second;
                        if (!seen.Add(key))
                            continue;

                        var report = Compare(apps[first], apps[second]);
                        if (report != null)
                            reports.Add(report);
                    }
                }
            }
        }

        private static void AddToGroup(Dictionary<string, List<int>> groups, string key, int index)
        {
            List<int> group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new List<int>();
                groups[key] = group;
            }

            if (group.Count == 0 || group[group.Count - 1] != index)
                group.Add(index);
        }

        /// <summary>Returns the set of rule ids involved in any conflict.</summary>
        public HashSet<System.Guid> ConflictingRuleIds(IEnumerable<RuleBase> rules)
        {
            var set = new HashSet<System.Guid>();
            foreach (var report in Detect(rules))
            {
                if (report.First != null) set.Add(report.First.Id);
                if (report.Second != null) set.Add(report.Second.Id);
            }
            return set;
        }

        private static ConflictReport Compare(RuleBase a, RuleBase b)
        {
            if (a.Module != b.Module)
                return null;

            var customA = a as CustomRegistryRule;
            var customB = b as CustomRegistryRule;
            if (customA != null && customB != null)
                return CompareCustom(customA, customB);

            // NameSpace rules are intentionally excluded: they only ever delete matching
            // entries, so overlapping targets still produce the same end state and can
            // never truly conflict. Comparing only their paths (the common case is several
            // rules sharing one NameSpace root) caused false positives that blocked them.
            var appA = a as DefaultAppRule;
            var appB = b as DefaultAppRule;
            if (appA != null && appB != null)
            {
                var extensionsA = NormalizeExtensions(appA.Extensions);
                var extensionsB = NormalizeExtensions(appB.Extensions);
                var different = extensionsA
                    .Where(ext => extensionsB.Contains(ext))
                    .Where(ext => !string.Equals(
                        appA.ResolveProgId(ext), appB.ResolveProgId(ext), System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (different.Count > 0)
                {
                    return new ConflictReport
                    {
                        First = a,
                        Second = b,
                        Description = "extensions " + string.Join(",", different) + " map to different ProgIds"
                    };
                }

                var protocolsA = NormalizeProtocols(appA.Protocols);
                var protocolsB = NormalizeProtocols(appB.Protocols);
                var protocolA = appA.ResolveProtocolProgId();
                var protocolB = appB.ResolveProtocolProgId();
                var differentProtocols = protocolsA
                    .Where(protocol => protocolsB.Contains(protocol))
                    .Where(protocol => !string.Equals(protocolA, protocolB, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (differentProtocols.Count > 0)
                {
                    return new ConflictReport
                    {
                        First = a,
                        Second = b,
                        Description = "protocols " + string.Join(",", differentProtocols) + " map to different ProgIds"
                    };
                }
            }

            return null;
        }

        private static List<string> NormalizeProtocols(IEnumerable<string> protocols)
        {
            var result = new List<string>();
            foreach (var raw in protocols ?? Enumerable.Empty<string>())
            {
                var protocol = DefaultAppRule.NormalizeProtocol(raw);
                if (protocol != null && !result.Contains(protocol, System.StringComparer.OrdinalIgnoreCase))
                    result.Add(protocol);
            }

            return result;
        }

        private static List<string> NormalizeExtensions(IEnumerable<string> extensions)
        {
            var result = new List<string>();
            foreach (var raw in extensions ?? Enumerable.Empty<string>())
            {
                var ext = DefaultAppRule.NormalizeExtension(raw);
                if (ext != null && !result.Contains(ext, System.StringComparer.OrdinalIgnoreCase))
                    result.Add(ext);
            }

            return result;
        }

        private static ConflictReport CompareCustom(CustomRegistryRule a, CustomRegistryRule b)
        {
            if (!string.Equals(a.ValueName ?? string.Empty, b.ValueName ?? string.Empty,
                    System.StringComparison.OrdinalIgnoreCase))
                return null;

            var sameOutcome = a.MatchType == b.MatchType
                              && string.Equals(a.ExpectedData, b.ExpectedData, System.StringComparison.Ordinal)
                              && a.Action == b.Action;

            if (sameOutcome)
                return null;

            return ComparePaths(a, b, a.KeyPatterns, b.KeyPatterns, "key patterns overlap with different expected states");
        }

        private static ConflictReport ComparePaths(
            RuleBase a, RuleBase b, IEnumerable<string> patternsA, IEnumerable<string> patternsB, string message)
        {
            foreach (var pa in patternsA ?? Enumerable.Empty<string>())
            {
                foreach (var pb in patternsB ?? Enumerable.Empty<string>())
                {
                    if (PatternsOverlap(pa, pb))
                    {
                        return new ConflictReport { First = a, Second = b, Description = message };
                    }
                }
            }

            return null;
        }

        private static bool PatternsOverlap(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            // If neither has a wildcard, compare directly.
            if (!Wildcard.HasWildcard(a) && !Wildcard.HasWildcard(b))
                return string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

            // Otherwise test each against the other and their static prefixes.
            if (Wildcard.IsMatch(a, b) || Wildcard.IsMatch(b, a))
                return true;

            var prefixA = Wildcard.StaticPrefix(a);
            var prefixB = Wildcard.StaticPrefix(b);

            return prefixA.StartsWith(prefixB, System.StringComparison.OrdinalIgnoreCase)
                   || prefixB.StartsWith(prefixA, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
