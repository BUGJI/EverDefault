using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    internal sealed class SaveApplyOutcome
    {
        public bool Saved;

        public bool Declined;

        public RunNowResult Result;

        public string Error;
    }

    /// <summary>
    /// Saves rules and applies the same rules once, so an edit takes effect immediately.
    /// Destructive rules (namespace cleanup / delete) need explicit confirmation first.
    /// </summary>
    internal static class RuleApply
    {
        public static bool IsDestructive(RuleBase rule)
        {
            if (rule == null)
                return false;

            if (rule.Module == RuleModule.NameSpace)
                return true;

            var custom = rule as CustomRegistryRule;
            return custom != null && (custom.Action == RuleAction.Delete
                                      || custom.OnMismatch == RuleAction.Delete);
        }

        public static async Task<SaveApplyOutcome> SaveAndRunAsync(
            IAppHost host, IEnumerable<RuleBase> rules,
            string savedMessage = null, bool showMessages = true)
        {
            var outcome = new SaveApplyOutcome();
            var list = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => r != null)
                .ToList();
            if (list.Count == 0)
            {
                outcome.Error = "没有可保存的规则。";
                return outcome;
            }

            var payload = list.Select(RuleCodec.Serialize).ToList();
            var destructive = list.Any(IsDestructive);
            IpcResponse response;

            if (destructive)
            {
                // Save first so the edit is kept even when the user declines the apply.
                response = list.Count == 1
                    ? await host.SendAsync(IpcProtocol.CommandSaveRule, payload[0])
                    : await host.SendAsync(IpcProtocol.CommandSaveRules, payload);

                outcome.Saved = response.Success;
                outcome.Error = response.Error;
                if (!outcome.Saved)
                {
                    if (showMessages)
                        ShowOutcome(host, outcome, savedMessage);
                    return outcome;
                }

                if (!host.Confirm(
                    "已保存。\n\n其中包含会删除注册表项的保护，立即应用可能会移除匹配到的项目。\n\n是否立即应用？"))
                {
                    outcome.Declined = true;
                    if (showMessages)
                        ShowOutcome(host, outcome, savedMessage);
                    return outcome;
                }

                var ids = list.Select(r => r.Id.ToString()).ToList();
                response = list.Count == 1
                    ? await host.SendAsync(IpcProtocol.CommandRunRule, ids[0])
                    : await host.SendAsync(IpcProtocol.CommandRunRules, ids);
            }
            else
            {
                response = list.Count == 1
                    ? await host.SendAsync(IpcProtocol.CommandSaveRuleAndRun, payload[0])
                    : await host.SendAsync(IpcProtocol.CommandSaveRulesAndRun, payload);

                outcome.Saved = response.Success;
                outcome.Error = response.Error;
            }

            if (response.Success)
            {
                outcome.Result = PipeJson.Deserialize<RunNowResult>(response.Payload);
            }
            else
            {
                outcome.Error = response.Error;
                if (outcome.Saved)
                {
                    outcome.Result = new RunNowResult
                    {
                        Message = "已保存，但应用失败：" + response.Error
                    };
                }
            }

            if (showMessages)
                ShowOutcome(host, outcome, savedMessage);

            return outcome;
        }

        private static void ShowOutcome(
            IAppHost host, SaveApplyOutcome outcome, string savedMessage)
        {
            if (!outcome.Saved)
            {
                host.ShowWarning("保存失败：" + (outcome.Error ?? "未知错误"));
                return;
            }

            if (outcome.Declined)
            {
                host.ShowInfo(Join(savedMessage, "已保存，未立即应用。"));
                return;
            }

            var result = outcome.Result;
            if (result == null)
            {
                host.ShowInfo(Join(savedMessage, "已保存。"));
                return;
            }

            var text = Join(savedMessage, result.Message);
            if (result.Started && result.Failed == 0)
                host.ShowInfo(text);
            else
                host.ShowWarning(text);
        }

        private static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
                return second ?? string.Empty;

            if (string.IsNullOrEmpty(second))
                return first;

            return first + "\n" + second;
        }
    }
}
