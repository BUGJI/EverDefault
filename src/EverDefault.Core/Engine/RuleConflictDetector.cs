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

            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var report = Compare(list[i], list[j]);
                    if (report != null)
                        reports.Add(report);
                }
            }

            return reports;
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
            }

            return null;
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
