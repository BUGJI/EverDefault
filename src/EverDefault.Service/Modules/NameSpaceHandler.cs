using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EverDefault.Core.Engine;
using EverDefault.Core.Model;
using EverDefault.Registry.NameSpace;
using EverDefault.Registry.Native;

namespace EverDefault.Service.Modules
{
    /// <summary>Deletes matching "This PC" / Desktop namespace entries.</summary>
    public sealed class NameSpaceHandler : IRuleModuleHandler
    {
        private readonly ModuleContext _ctx;

        public NameSpaceHandler(ModuleContext ctx)
        {
            _ctx = ctx;
        }

        public RuleModule Module
        {
            get { return RuleModule.NameSpace; }
        }

        public IEnumerable<WatchRegistration> GetWatchTargets(RuleBase rule)
        {
            foreach (var root in ResolveRoots(rule as NameSpaceRule))
                yield return new WatchRegistration { KeyPath = root, Recursive = false };
        }

        public void CaptureBaseline(RuleBase rule)
        {
            // Namespace rules are match-and-delete; no baseline is required.
        }

        public void Handle(RuleBase rule, ChangeOrigin origin)
        {
            var ns = rule as NameSpaceRule;
            if (ns == null)
                return;

            var roots = ResolveRoots(ns).ToList();
            var entries = NameSpaceScanner.Scan(_ctx.Registry, roots);
            var changed = false;

            foreach (var entry in entries)
            {
                if (!Matches(ns, entry))
                    continue;

                try
                {
                    _ctx.Registry.DeleteKey(entry.NameSpaceKeyPath, false);

                    if (ns.DeleteScope >= NameSpaceDeleteScope.WithClsid && !string.IsNullOrEmpty(entry.ClsidKeyPath))
                        _ctx.Registry.DeleteKey(entry.ClsidKeyPath, true);

                    // WithClsidAndShellFolder is a superset handled by the recursive CLSID delete today.
                    _ctx.WriteLog(rule, entry.NameSpaceKeyPath, entry.DisplayName, entry.Guid, null,
                        "Delete", "deleted", origin, "namespace entry removed");
                    changed = true;
                }
                catch (Exception ex)
                {
                    _ctx.WriteLog(rule, entry.NameSpaceKeyPath, entry.DisplayName, entry.Guid, null,
                        "Delete", "failed", origin, ex.Message);
                    _ctx.WriteTrace("namespace delete failed: " + ex.Message);
                }
            }

            if (changed && ns.RefreshShell)
            {
                try
                {
                    ShellRefresh.NotifyAssociationsChanged();
                }
                catch (Exception ex)
                {
                    _ctx.WriteTrace("SHChangeNotify failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Expands HKEY_CURRENT_USER roots into HKEY_USERS\&lt;SID&gt; for every target user,
        /// mirroring <see cref="DefaultAppHandler"/>. Without this, a rule that uses HKCU would
        /// scan the service account's own hive instead of the logged-on users'.
        /// </summary>
        private IEnumerable<string> ResolveRoots(NameSpaceRule ns)
        {
            IEnumerable<string> raw = (ns != null && ns.PathPatterns != null && ns.PathPatterns.Count > 0)
                ? (IEnumerable<string>)ns.PathPatterns
                : NameSpaceScanner.DefaultRoots;

            var scope = _ctx.UserScope;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in raw)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var root = candidate.Trim();
                if (scope == null)
                {
                    if (seen.Add(root))
                        yield return root;
                    continue;
                }

                foreach (var expanded in scope.ExpandCurrentUser(root))
                {
                    if (seen.Add(expanded))
                        yield return expanded;
                }
            }
        }

        private static bool Matches(NameSpaceRule rule, NameSpaceEntry entry)
        {
            var pattern = rule.MatchPattern ?? string.Empty;

            switch (rule.MatchType)
            {
                case NameSpaceMatchType.Guid:
                    return string.Equals(entry.Guid, pattern, StringComparison.OrdinalIgnoreCase);
                case NameSpaceMatchType.DisplayName:
                    return (entry.DisplayName ?? string.Empty)
                        .IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                case NameSpaceMatchType.TargetPath:
                    return (entry.TargetPath ?? string.Empty)
                        .IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                case NameSpaceMatchType.Regex:
                    var haystack = string.Join(" ", entry.Guid, entry.DisplayName, entry.TargetPath);
                    return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase);
                default:
                    return false;
            }
        }
    }
}
