using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Engine;
using EverDefault.Core.Model;
using EverDefault.Core.Util;
using EverDefault.Registry.Access;

namespace EverDefault.Service.Modules
{
    /// <summary>
    /// Custom-registry rules. Desired state comes from the rule's ExpectedData when a
    /// concrete match type is used, otherwise from the baseline captured at save time.
    /// </summary>
    public sealed class CustomRegistryHandler : IRuleModuleHandler
    {
        private const char Separator = '\u0000';

        private readonly ModuleContext _ctx;

        public CustomRegistryHandler(ModuleContext ctx)
        {
            _ctx = ctx;
        }

        public RuleModule Module
        {
            get { return RuleModule.CustomRegistry; }
        }

        public IEnumerable<WatchRegistration> GetWatchTargets(RuleBase rule)
        {
            var custom = rule as CustomRegistryRule;
            if (custom == null || custom.KeyPatterns == null)
                yield break;

            foreach (var pattern in ResolvePatterns(custom.KeyPatterns))
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                var root = Wildcard.HasWildcard(pattern) ? Wildcard.StaticPrefix(pattern) : pattern;
                if (!string.IsNullOrEmpty(root))
                    yield return new WatchRegistration { KeyPath = root, Recursive = true };
            }
        }

        /// <summary>
        /// Expands an HKCU pattern to one HKEY_USERS\&lt;SID&gt; pattern per target user so a
        /// service (SYSTEM) can watch/enforce the right profiles. Non-HKCU paths pass through.
        /// </summary>
        private IEnumerable<string> ResolvePatterns(IEnumerable<string> patterns)
        {
            if (patterns == null || _ctx.UserScope == null)
                yield break;

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                if (!Wildcard.HasWildcard(pattern))
                {
                    foreach (var expanded in _ctx.UserScope.ExpandCurrentUser(pattern))
                        yield return expanded;
                    continue;
                }

                var prefix = Wildcard.StaticPrefix(pattern);
                var suffix = pattern.Substring(prefix.Length);
                foreach (var expanded in _ctx.UserScope.ExpandCurrentUser(prefix))
                    yield return expanded + suffix;
            }
        }

        public void CaptureBaseline(RuleBase rule)
        {
            var custom = rule as CustomRegistryRule;
            if (custom == null)
                return;

            var values = RegistrySnapshotter.Capture(_ctx.Registry, ResolvePatterns(custom.KeyPatterns), true);
            var records = values.Select(v => new BaselineRecord
            {
                KeyPath = v.KeyPath,
                ValueName = v.Name,
                Exists = v.Exists,
                ValueKind = v.Kind,
                Data = v.Data,
                KeyLastWriteTimeUtc = v.KeyLastWriteTimeUtc
            }).ToList();

            _ctx.Baselines.ReplaceForRule(rule.Id, records);

            // "WritePolicy" is enforced proactively on save as well as on mismatch, so the
            // policy is asserted even before anything changes the monitored key.
            if (EffectiveAction(custom) == RuleAction.WritePolicy)
                SeedPolicy(custom, records);
        }

        private static RuleAction EffectiveAction(CustomRegistryRule rule)
        {
            return rule.OnMismatch != RuleAction.Restore ? rule.OnMismatch : rule.Action;
        }

        /// <summary>Writes the desired value to the policy key when the rule is saved.</summary>
        private void SeedPolicy(CustomRegistryRule rule, List<BaselineRecord> records)
        {
            if (_ctx.Settings != null && !_ctx.Settings.MonitoringEnabled)
                return;

            if (string.IsNullOrWhiteSpace(rule.PolicyKey))
                return;

            string kind;
            string data;

            if (rule.ExpectedData != null)
            {
                data = rule.ExpectedData;
                kind = !string.IsNullOrWhiteSpace(rule.ExpectedKind) ? rule.ExpectedKind : null;
            }
            else
            {
                var match = records.FirstOrDefault(r => r.Exists
                    && string.Equals(r.ValueName ?? string.Empty, rule.ValueName ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return;

                data = match.Data;
                kind = match.ValueKind;
            }

            if (string.IsNullOrWhiteSpace(kind))
                return;

            var current = new RegistryValueData { KeyPath = rule.PolicyKey, Name = rule.ValueName, Exists = false };
            var desired = new BaselineRecord
            {
                KeyPath = rule.PolicyKey,
                ValueName = rule.ValueName,
                Exists = true,
                ValueKind = kind,
                Data = data
            };

            WritePolicyValue(rule, rule, current, desired, ChangeOrigin.Manual, "保存规则时预写策略");
        }

        public void Handle(RuleBase rule, ChangeOrigin origin)
        {
            var custom = rule as CustomRegistryRule;
            if (custom == null)
                return;

            var current = RegistrySnapshotter.Capture(_ctx.Registry, ResolvePatterns(custom.KeyPatterns), true);
            var currentMap = current.ToDictionary(v => v.KeyPath + Separator + v.Name, v => v,
                StringComparer.OrdinalIgnoreCase);

            var baseline = _ctx.Baselines.GetForRule(rule.Id)
                .ToDictionary(b => b.KeyPath + Separator + b.ValueName, b => b,
                    StringComparer.OrdinalIgnoreCase);

            var expectedMode = custom.MatchType != ValueMatchType.AnyChange && custom.ExpectedData != null;

            // Present (or newly added) values.
            foreach (var value in current)
            {
                BaselineRecord desired;
                if (expectedMode)
                {
                    if (!MatchesExpected(custom, value))
                    {
                        desired = new BaselineRecord
                        {
                            KeyPath = value.KeyPath,
                            ValueName = value.Name,
                            Exists = true,
                            ValueKind = custom.ExpectedKind ?? value.Kind,
                            Data = custom.ExpectedData
                        };
                        Apply(custom, rule, value, desired, origin, "expected value mismatch");
                    }
                }
                else if (baseline.TryGetValue(value.KeyPath + Separator + value.Name, out desired))
                {
                    if (!string.Equals(desired.Data, value.Data, StringComparison.Ordinal)
                        || !string.Equals(desired.ValueKind, value.Kind, StringComparison.Ordinal))
                    {
                        Apply(custom, rule, value, desired, origin, "value changed");
                    }
                }
                else
                {
                    // Value appeared that was not in the baseline: baseline says "absent".
                    Apply(custom, rule, value, null, origin, "unexpected new value");
                }
            }

            // Values removed since the baseline.
            foreach (var record in baseline.Values)
            {
                if (currentMap.ContainsKey(record.KeyPath + Separator + record.ValueName))
                    continue;

                var removed = new RegistryValueData
                {
                    KeyPath = record.KeyPath,
                    Name = record.ValueName,
                    Exists = false
                };
                Apply(custom, rule, removed, record, origin, "value deleted");
            }
        }

        private static bool MatchesExpected(CustomRegistryRule rule, RegistryValueData value)
        {
            var expected = rule.ExpectedData ?? string.Empty;

            switch (rule.MatchType)
            {
                case ValueMatchType.Exact:
                    return string.Equals(value.Data, expected, StringComparison.Ordinal);
                case ValueMatchType.Contains:
                    return (value.Data ?? string.Empty).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
                case ValueMatchType.Regex:
                    return System.Text.RegularExpressions.Regex.IsMatch(
                        value.Data ?? string.Empty, expected,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                default:
                    return true;
            }
        }

        private void Apply(
            CustomRegistryRule rule,
            RuleBase baseRule,
            RegistryValueData current,
            BaselineRecord desired,
            ChangeOrigin origin,
            string reason)
        {
            var action = rule.OnMismatch != RuleAction.Restore ? rule.OnMismatch : baseRule.Action;

            if (_ctx.Settings != null && !_ctx.Settings.MonitoringEnabled)
                return;

            try
            {
                string result;
                switch (action)
                {
                    case RuleAction.LogOnly:
                        result = "logged";
                        break;

                    case RuleAction.Delete:
                        _ctx.Registry.DeleteValue(current.KeyPath, current.Name);
                        result = "deleted";
                        break;

                    case RuleAction.WritePolicy:
                        WritePolicyValue(rule, baseRule, current, desired, origin, reason);
                        return;

                    default:
                        if (desired != null && desired.Exists)
                        {
                            _ctx.Registry.WriteValue(current.KeyPath, current.Name,
                                desired.ValueKind ?? current.Kind, desired.Data);
                            result = "restored";
                        }
                        else
                        {
                            _ctx.Registry.DeleteValue(current.KeyPath, current.Name);
                            result = "removed";
                        }
                        break;
                }

                _ctx.WriteLog(baseRule, current.KeyPath, current.Name, current.Data,
                    desired != null && desired.Exists ? desired.Data : null,
                    action.ToString(), result, origin, reason);
            }
            catch (Exception ex)
            {
                _ctx.WriteLog(baseRule, current.KeyPath, current.Name, current.Data, null,
                    action.ToString(), "failed", origin, reason + ": " + ex.Message);
                _ctx.WriteTrace("custom rule failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes the desired value to the rule's policy key so Windows enforces it, rather
        /// than merely reverting the monitored key. HKCU policy keys are expanded per target user.
        /// </summary>
        private void WritePolicyValue(
            CustomRegistryRule rule,
            RuleBase baseRule,
            RegistryValueData current,
            BaselineRecord desired,
            ChangeOrigin origin,
            string reason)
        {
            var valueName = rule.ValueName ?? string.Empty;
            var action = RuleAction.WritePolicy.ToString();

            if (string.IsNullOrWhiteSpace(rule.PolicyKey))
            {
                _ctx.WriteLog(baseRule, rule.PolicyKey, valueName, current.Data, null,
                    action, "failed", origin, "未配置策略键(PolicyKey)");
                return;
            }

            if (desired == null || !desired.Exists)
            {
                _ctx.WriteLog(baseRule, rule.PolicyKey, valueName, current.Data, null,
                    action, "failed", origin, "写策略需要期望值或基线值");
                return;
            }

            var kind = !string.IsNullOrWhiteSpace(desired.ValueKind) ? desired.ValueKind : current.Kind;

            var targets = ResolvePatterns(new[] { rule.PolicyKey })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targets.Count == 0)
                targets.Add(rule.PolicyKey);

            foreach (var target in targets)
            {
                try
                {
                    _ctx.Registry.WriteValue(target, valueName, kind, desired.Data);
                    _ctx.WriteLog(baseRule, target, valueName, current.Data, desired.Data,
                        action, "restored", origin, "策略已写入: " + reason);
                }
                catch (Exception ex)
                {
                    _ctx.WriteLog(baseRule, target, valueName, current.Data, null,
                        action, "failed", origin, "策略写入失败: " + ex.Message);
                    _ctx.WriteTrace("policy write failed: " + ex.Message);
                }
            }
        }
    }
}
