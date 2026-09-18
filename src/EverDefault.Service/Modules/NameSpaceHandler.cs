using System;
using System.Collections.Generic;
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
            var ns = rule as NameSpaceRule;
            var roots = (ns != null && ns.PathPatterns != null && ns.PathPatterns.Count > 0)
                ? (IEnumerable<string>)ns.PathPatterns
                : NameSpaceScanner.DefaultRoots;

            foreach (var root in roots)
            {
                if (!string.IsNullOrWhiteSpace(root))
                    yield return new WatchRegistration { KeyPath = root, Recursive = false };
            }
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

            var roots = (ns.PathPatterns != null && ns.PathPatterns.Count > 0) ? ns.PathPatterns : null;
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
