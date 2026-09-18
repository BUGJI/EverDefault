using System;
using System.Collections.Generic;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>Maps core enums and raw engine strings to user-facing Chinese text.</summary>
    internal static class Display
    {
        public static string Module(RuleModule module)
        {
            switch (module)
            {
                case RuleModule.DefaultApp: return "默认应用";
                case RuleModule.NameSpace: return "命名空间";
                default: return "自定义注册表";
            }
        }

        public static string Mode(RuleMode mode)
        {
            switch (mode)
            {
                case RuleMode.Schedule: return "定时轮询";
                case RuleMode.Manual: return "手动";
                default: return "监控";
            }
        }

        public static string Action(RuleAction action)
        {
            switch (action)
            {
                case RuleAction.Restore: return "还原";
                case RuleAction.Delete: return "删除";
                default: return "仅记录";
            }
        }

        public static string ValueMatch(ValueMatchType type)
        {
            switch (type)
            {
                case ValueMatchType.Exact: return "精确匹配";
                case ValueMatchType.Contains: return "包含";
                case ValueMatchType.Regex: return "正则";
                default: return "任意改动";
            }
        }

        public static string NsMatch(NameSpaceMatchType type)
        {
            switch (type)
            {
                case NameSpaceMatchType.DisplayName: return "显示名称";
                case NameSpaceMatchType.TargetPath: return "目标路径";
                case NameSpaceMatchType.Regex: return "正则";
                default: return "GUID";
            }
        }

        public static string NsScope(NameSpaceDeleteScope scope)
        {
            switch (scope)
            {
                case NameSpaceDeleteScope.WithClsid: return "同时删除 CLSID";
                case NameSpaceDeleteScope.WithClsidAndShellFolder: return "CLSID + ShellFolder";
                default: return "仅命名空间项";
            }
        }

        public static string Os(OsFamily family)
        {
            switch (family)
            {
                case OsFamily.Win7: return "Windows 7";
                case OsFamily.Win8: return "Windows 8";
                case OsFamily.Win10: return "Windows 10";
                case OsFamily.Win11: return "Windows 11";
                default: return "自动检测";
            }
        }

        public static string UserScope(UserScopeMode scope)
        {
            switch (scope)
            {
                case UserScopeMode.CurrentUser: return "仅当前用户";
                default: return "所有已登录用户";
            }
        }

        /// <summary>Localizes the raw action string written by the engine into the change log.</summary>
        public static string LogAction(string action)
        {
            switch (action)
            {
                case "Restore": return "还原";
                case "Delete": return "删除";
                case "LogOnly": return "仅记录";
                default: return action;
            }
        }

        /// <summary>Localizes the raw result string written by the engine into the change log.</summary>
        public static string LogResult(string result)
        {
            switch (result)
            {
                case "logged": return "已记录";
                case "deleted": return "已删除";
                case "restored": return "已还原";
                case "removed": return "已移除";
                case "failed": return "失败";
                default: return result;
            }
        }

        public static List<EnumOption> Options<TEnum>(Func<TEnum, string> text)
        {
            var options = new List<EnumOption>();
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
                options.Add(new EnumOption { Value = value, Text = text(value) });

            return options;
        }
    }

    /// <summary>Display text paired with the underlying enum value for combo boxes.</summary>
    public sealed class EnumOption
    {
        public object Value { get; set; }

        public string Text { get; set; }
    }
}
