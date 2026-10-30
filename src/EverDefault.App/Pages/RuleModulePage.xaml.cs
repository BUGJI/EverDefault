using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    /// <summary>One rule category: the list on the left and an inline editor on the right.</summary>
    public partial class RuleModulePage : UserControl, IAppPage
    {
        private readonly IAppHost _host;
        private readonly RuleModule _module;
        private readonly ObservableCollection<RuleRow> _viewRows = new ObservableCollection<RuleRow>();
        private bool _serviceRunning;
        private Guid? _selectedId;
        private bool _suppressSelection;
        private bool _suppressFilter;
        private List<RuleRow> _allRows = new List<RuleRow>();

        public RuleModulePage(IAppHost host, RuleModule module)
        {
            _host = host;
            _module = module;
            InitializeComponent();
            RulesList.ItemsSource = _viewRows;

            TitleText.Text = Display.Module(module);
            DescText.Text = Describe(module);
            TestRuleMenuItem.Visibility = module == RuleModule.CustomRegistry
                ? Visibility.Visible
                : Visibility.Collapsed;
            ImportFormatsMenuItem.Visibility = module == RuleModule.DefaultApp
                ? Visibility.Visible
                : Visibility.Collapsed;
            GroupApplyMenuItem.Visibility = module == RuleModule.DefaultApp
                ? Visibility.Visible
                : Visibility.Collapsed;
            ChangeAppMenuItem.Visibility = module == RuleModule.DefaultApp
                ? Visibility.Visible
                : Visibility.Collapsed;
            MoreButton.Visibility = module == RuleModule.DefaultApp
                                    || module == RuleModule.CustomRegistry
                ? Visibility.Visible
                : Visibility.Collapsed;

            _suppressFilter = true;
            FilterBox.SelectedIndex = 0;
            _suppressFilter = false;

            Editor.Configure(module);
            Editor.SetInlineMode(true);
            Editor.SaveRequested += OnEditorSave;
            Editor.ResetRequested += OnEditorReset;

            UpdateButtons();
        }

        private static string Describe(RuleModule module)
        {
            switch (module)
            {
                case RuleModule.DefaultApp:
                    return "锁定文件类型的默认打开程序，被其他软件改动后自动还原。";
                case RuleModule.NameSpace:
                    return "检测并删除“此电脑/桌面”下多余的自定义命名空间项。";
                default:
                    return "按键模式监控任意注册表值，可按基线或期望值还原/删除/仅记录。";
            }
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;

            if (snapshot.Rules != null)
            {
                _allRows = snapshot.Rules.Where(r => r.Rule.Module == _module).ToList();

                if (_selectedId.HasValue && !_allRows.Any(r => r.Rule.Id == _selectedId.Value))
                {
                    _selectedId = null;
                    Editor.Clear();
                }

                RebuildView();
            }
            else
            {
                RebuildView();
            }

            OfflinePanel.Visibility = _serviceRunning ? Visibility.Collapsed : Visibility.Visible;
            Editor.IsEnabled = _serviceRunning;
            UpdateButtons();
        }

        private void RebuildView()
        {
            var filtered = _allRows.Where(Matches).ToList();
            var currentIds = _viewRows.Select(r => r.Rule.Id).ToList();
            var newIds = filtered.Select(r => r.Rule.Id).ToList();

            _suppressSelection = true;
            try
            {
                if (!currentIds.SequenceEqual(newIds))
                {
                    _viewRows.Clear();
                    foreach (var row in filtered)
                        _viewRows.Add(row);
                }
                else
                {
                    for (var i = 0; i < filtered.Count; i++)
                    {
                        if (!ReferenceEquals(_viewRows[i].Rule, filtered[i].Rule))
                            _viewRows[i].Update(filtered[i].Rule);
                    }
                }

                if (_selectedId.HasValue)
                    RulesList.SelectedItem = _viewRows.FirstOrDefault(r => r.Rule.Id == _selectedId.Value);
            }
            finally
            {
                _suppressSelection = false;
            }

            CountText.Text = filtered.Count == _allRows.Count
                ? "共 " + _allRows.Count + " 条"
                : "显示 " + filtered.Count + " / " + _allRows.Count + " 条";

            if (!_serviceRunning)
                EmptyHint.Text = "后台服务未运行，无法读取规则。";
            else if (_allRows.Count == 0)
                EmptyHint.Text = "还没有规则。点『新建』创建第一条，或在『更多』中体验测试规则。";
            else if (filtered.Count == 0)
                EmptyHint.Text = "没有匹配的规则，试试其他关键词或筛选条件。";
            else
                EmptyHint.Text = string.Empty;
        }

        private bool Matches(RuleRow row)
        {
            var text = SearchBox.Text.Trim();
            if (text.Length > 0
                && !Contains(row.Name, text)
                && !Contains(row.Target, text)
                && !Contains(row.Summary, text))
            {
                return false;
            }

            var item = FilterBox.SelectedItem as ComboBoxItem;
            var tag = item == null ? null : item.Tag as string;
            if (string.Equals(tag, "enabled", StringComparison.OrdinalIgnoreCase))
                return row.Rule.Enabled;
            if (string.Equals(tag, "disabled", StringComparison.OrdinalIgnoreCase))
                return !row.Rule.Enabled;

            return true;
        }

        private static bool Contains(string value, string text)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private RuleRow CurrentRule()
        {
            if (!_selectedId.HasValue)
                return null;

            return _allRows.FirstOrDefault(r => r.Rule.Id == _selectedId.Value);
        }

        private void UpdateButtons()
        {
            var current = CurrentRule();

            NewButton.IsEnabled = _serviceRunning;
            MoreButton.IsEnabled = _serviceRunning;
            DeleteButton.IsEnabled = _serviceRunning && current != null;
            ChangeAppMenuItem.IsEnabled = _serviceRunning && current != null && current.Rule is DefaultAppRule;
            GroupApplyMenuItem.IsEnabled = _serviceRunning;
            ImportFormatsMenuItem.IsEnabled = _serviceRunning;
            TestRuleMenuItem.IsEnabled = _serviceRunning;
            Editor.CanSave = _serviceRunning;
        }

        private bool ConfirmDiscard(string action)
        {
            if (!Editor.IsDirty)
                return true;

            return _host.Confirm("当前规则有未保存的修改，" + action + "将丢失这些修改。是否继续？");
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;

            var row = RulesList.SelectedItem as RuleRow;
            if (row == null)
                return;

            if (_selectedId.HasValue && _selectedId.Value == row.Rule.Id)
            {
                UpdateButtons();
                return;
            }

            if (!ConfirmDiscard("切换到其他规则"))
            {
                _suppressSelection = true;
                RulesList.SelectedItem = _selectedId.HasValue
                    ? _viewRows.FirstOrDefault(r => r.Rule.Id == _selectedId.Value)
                    : null;
                _suppressSelection = false;
                return;
            }

            _selectedId = row.Rule.Id;
            Editor.LoadRule(row.Rule);
            UpdateButtons();
        }

        private void OnNew(object sender, RoutedEventArgs e)
        {
            if (!_serviceRunning)
                return;

            if (!ConfirmDiscard("新建规则"))
                return;

            _suppressSelection = true;
            RulesList.SelectedItem = null;
            _selectedId = null;
            _suppressSelection = false;

            Editor.BeginNew();
            UpdateButtons();
        }

        private async void OnDelete(object sender, RoutedEventArgs e)
        {
            var current = CurrentRule();
            if (current == null)
            {
                _host.ShowInfo("请先选择要删除的规则。");
                return;
            }

            if (!_host.Confirm("删除规则 \"" + current.Name + "\"？删除后不再保护它的目标。"))
                return;

            var response = await _host.SendAsync(IpcProtocol.CommandDeleteRule, current.Rule.Id.ToString());
            if (!response.Success)
            {
                _host.ShowWarning("服务未运行，规则未删除：\n" + response.Error);
                return;
            }

            _selectedId = null;
            Editor.Clear();
            _host.Refresh();
        }

        private async void OnEditorSave(object sender, EventArgs e)
        {
            string error;
            var rule = Editor.BuildRule(out error);
            if (rule == null)
            {
                Editor.ShowError(error);
                return;
            }

            var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
            if (!response.Success)
            {
                Editor.ShowError("服务未运行，规则未保存：\n" + response.Error);
                return;
            }

            Editor.AcceptSaved(rule);
            Editor.ShowStatus("已保存");
            _selectedId = rule.Id;
            _host.Refresh();
            UpdateButtons();
        }

        private void OnEditorReset(object sender, EventArgs e)
        {
            var row = CurrentRule();
            if (row != null)
                Editor.LoadRule(row.Rule);
            else if (Editor.IsNewDraft)
                Editor.BeginNew();
            else
                Editor.Clear();

            UpdateButtons();
        }

        private async void OnChangeApp(object sender, RoutedEventArgs e)
        {
            var current = CurrentRule();
            var primary = current == null ? null : current.Rule as DefaultAppRule;
            if (primary == null)
            {
                _host.ShowInfo("请先选择一条默认应用规则。");
                return;
            }

            var extensions = new List<string>();
            foreach (var extension in primary.Extensions)
            {
                var ext = DefaultAppRule.NormalizeExtension(extension);
                if (ext != null && !extensions.Contains(ext))
                    extensions.Add(ext);
            }

            if (extensions.Count == 0)
            {
                _host.ShowInfo("该规则没有可换的扩展名。");
                return;
            }

            var window = new ChangeAppWindow(extensions, primary.AppName ?? primary.ProgId)
            {
                Owner = Window.GetWindow(this)
            };
            if (window.ShowDialog() != true)
                return;

            var updated = new DefaultAppRule
            {
                Id = primary.Id,
                Name = RuleNaming.ForDefaultApp(window.Extensions, null, window.AppName),
                Enabled = primary.Enabled,
                Mode = primary.Mode,
                Action = primary.Action,
                IntervalSeconds = primary.IntervalSeconds,
                Priority = primary.Priority,
                MinOs = primary.MinOs,
                MaxOs = primary.MaxOs,
                CreatedUtc = primary.CreatedUtc,
                UpdatedUtc = DateTime.UtcNow,
                AppName = window.AppName,
                AppPath = window.AppPath,
                Extensions = window.Extensions,
                ProgIdMap = window.ProgIdMap,
                Protocols = primary.Protocols,
                ProtocolProgId = primary.ProtocolProgId,
                ManageOpenWith = primary.ManageOpenWith,
                ManageFileAssociation = primary.ManageFileAssociation
            };

            var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(updated));
            if (!response.Success)
            {
                _host.ShowWarning("保存失败：\n" + response.Error);
                return;
            }

            _selectedId = updated.Id;
            Editor.LoadRule(updated);
            Editor.ShowStatus("已换应用");
            _host.Refresh();
            UpdateButtons();
        }

        private async void OnImportFormats(object sender, RoutedEventArgs e)
        {
            var window = new ApplicationFormatsWindow { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules);
        }

        private async void OnGroupApply(object sender, RoutedEventArgs e)
        {
            var window = new FormatGroupWindow { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules);
        }

        private async System.Threading.Tasks.Task SaveRules(IEnumerable<DefaultAppRule> rules)
        {
            var succeeded = 0;
            var errors = new List<string>();
            foreach (var rule in rules)
            {
                var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
                if (response.Success)
                    succeeded++;
                else
                    errors.Add(rule.Name + ": " + response.Error);
            }

            _host.Refresh();

            if (errors.Count > 0)
                _host.ShowWarning("生成完成：成功 " + succeeded + " 条，失败 " + errors.Count + " 条。\n"
                    + string.Join("\n", errors.Take(8)));
            else
                _host.ShowInfo("已生成 " + succeeded + " 条规则。");
        }

        private async void OnTestRule(object sender, RoutedEventArgs e)
        {
            var rule = new CustomRegistryRule
            {
                Name = "测试规则(仅记录)",
                Mode = RuleMode.Monitor,
                Enabled = true,
                Action = RuleAction.LogOnly,
                OnMismatch = RuleAction.LogOnly,
                MatchType = ValueMatchType.AnyChange,
                KeyPatterns = new List<string> { @"HKEY_CURRENT_USER\Software\EverDefaultTest" }
            };

            var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
            if (!response.Success)
            {
                _host.ShowWarning("服务未运行，无法创建测试规则：\n" + response.Error);
                return;
            }

            _host.ShowInfo(
                "已创建测试规则（仅记录，不改动）。\n\n" +
                "用 regedit 或命令行在下面这个键下新建/修改任意值，几秒后即可在“活动”页看到：\n" +
                @"HKEY_CURRENT_USER\Software\EverDefaultTest\");

            _selectedId = rule.Id;
            Editor.LoadRule(rule);
            _host.Refresh();
            UpdateButtons();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            RebuildView();
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilter)
                return;

            RebuildView();
        }

        private void OnMoreClick(object sender, RoutedEventArgs e)
        {
            var menu = MoreButton.ContextMenu;
            if (menu == null)
                return;

            menu.PlacementTarget = MoreButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CurrentRule() != null)
                Editor.FocusName();
        }

        private void OnPageKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OnNew(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Editor.CanSave && Editor.HasDraft)
                    OnEditorSave(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete
                && !(Keyboard.FocusedElement is TextBox)
                && !(Keyboard.FocusedElement is ComboBox)
                && DeleteButton.IsEnabled)
            {
                OnDelete(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            _host.Navigate("settings");
        }

        private void OnStartService(object sender, RoutedEventArgs e)
        {
            ServiceConnector.TryStart(_host);
        }
    }
}
