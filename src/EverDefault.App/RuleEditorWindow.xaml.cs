using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>
    /// The single rule-creation surface: a simplified "basic" tab and the raw "advanced" tab.
    /// Both tabs edit the same draft and return one or more rules on save.
    /// </summary>
    public partial class RuleEditorWindow : DialogWindow
    {
        private readonly RuleModule _module;
        private readonly RuleBase _existing;
        private RuleBase _draft;
        private bool _syncing;

        public RuleEditorWindow(IAppHost host, RuleModule module, RuleBase existing = null)
        {
            _module = module;
            _existing = existing;

            InitializeComponent();
            WindowSizing.Clamp(this);

            Basic.SetHost(host);
            Basic.Configure(module);
            Advanced.Configure(module);
            Advanced.SetEmbedded(true);

            Basic.AdvancedRequested += (s, e) => SelectAdvanced();
            Basic.SelectionChanged += (s, e) => AdvancedTab.IsEnabled = true;

            Title = existing == null ? "添加保护" : "编辑保护";
            SubText.Text = "基础配置适合大多数情况；需要精确控制时再切换到高级配置。";

            if (existing != null)
            {
                _draft = existing;
                Basic.LoadRule(existing);
                Advanced.LoadRule(existing);

                var appRule = existing as DefaultAppRule;
                var protocolRule = appRule != null
                                   && appRule.Protocols != null
                                   && appRule.Protocols.Count > 0;
                Tabs.SelectedIndex = module == RuleModule.DefaultApp && !protocolRule ? 0 : 1;
            }
            else
            {
                Tabs.SelectedIndex = 0;
            }
        }

        /// <summary>The rules the user confirmed.</summary>
        public List<RuleBase> Rules { get; private set; }

        private void SelectAdvanced()
        {
            AdvancedTab.IsEnabled = true;
            Tabs.SelectedIndex = 1;
        }

        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || !IsLoaded)
                return;

            _syncing = true;
            try
            {
                if (Tabs.SelectedIndex == 1)
                {
                    string error;
                    var rules = Basic.BuildRules(out error);
                    if (rules != null && rules.Count > 1)
                    {
                        ErrorText.Text = "当前选择了多个目标，保存时会分别创建保护。高级配置一次只能编辑一个目标。";
                        AdvancedTab.IsEnabled = false;
                        Tabs.SelectedIndex = 0;
                        return;
                    }

                    ErrorText.Text = string.Empty;
                    if (rules != null && rules.Count == 1)
                        _draft = rules[0];

                    if (_draft != null)
                        Advanced.LoadRule(_draft);
                }
                else
                {
                    string error;
                    var rule = Advanced.BuildRule(out error);
                    if (rule != null)
                    {
                        ErrorText.Text = string.Empty;
                        _draft = rule;
                        Basic.LoadRule(rule);
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            string error;
            List<RuleBase> rules;

            if (Tabs.SelectedIndex == 0)
            {
                rules = Basic.BuildRules(out error);
            }
            else
            {
                var rule = Advanced.BuildRule(out error);
                rules = rule == null ? null : new List<RuleBase> { rule };
            }

            if (rules == null || rules.Count == 0)
            {
                ErrorText.Text = error ?? "请先完成配置。";
                return;
            }

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Name))
                    rule.Name = RuleNaming.Suggest(rule);

                if (rule.IntervalSeconds <= 0)
                    rule.IntervalSeconds = 60;

                if (_existing != null && rules.Count == 1)
                {
                    rule.Id = _existing.Id;
                    rule.CreatedUtc = _existing.CreatedUtc;
                    rule.Priority = _existing.Priority;
                    rule.MinOs = _existing.MinOs;
                    rule.MaxOs = _existing.MaxOs;
                }
            }

            Rules = rules;
            DialogResult = true;
        }
    }
}
