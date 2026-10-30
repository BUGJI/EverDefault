using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    /// <summary>The friendly "my protections" list, grouped by application.</summary>
    public partial class ProtectionPage : UserControl, IAppPage
    {
        private readonly IAppHost _host;
        private List<ProtectionItem> _allItems = new List<ProtectionItem>();
        private bool _serviceRunning;

        public ProtectionPage(IAppHost host)
        {
            _host = host;
            InitializeComponent();
            FilterBox.SelectedIndex = 0;
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;
            OfflineBanner.Visibility = _serviceRunning ? Visibility.Collapsed : Visibility.Visible;
            OfflineText.Text = snapshot.ServiceStarting
                ? "正在启动后台保护，请稍候…"
                : "后台保护未运行，暂时无法读取或修改保护项。";

            if (!_serviceRunning)
            {
                _allItems = new List<ProtectionItem>();
                ProtectionList.ItemsSource = null;
                EmptyHint.Text = string.Empty;
            }
            else if (snapshot.Rules != null)
            {
                _allItems = ProtectionBuilder.BuildItems(snapshot.Rules.Select(r => r.Rule));
                ApplyFilter();
            }
            else
            {
                ApplyFilter();
            }

            ProtectCurrentButton.IsEnabled = _serviceRunning;
            AddAppButton.IsEnabled = _serviceRunning;
            AddFileTypeButton.IsEnabled = _serviceRunning;
            AddNameSpaceButton.IsEnabled = _serviceRunning;
            AddCustomButton.IsEnabled = _serviceRunning;
            ProtectionList.IsEnabled = _serviceRunning;
            SearchBox.IsEnabled = _serviceRunning;
            FilterBox.IsEnabled = _serviceRunning;

            PauseAllButton.IsEnabled = _serviceRunning && _allItems.Count > 0;
            ResumeAllButton.IsEnabled = _serviceRunning && _allItems.Any(i => i.Paused);
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyFilter();
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = (SearchBox.Text ?? string.Empty).Trim();
            var item = FilterBox.SelectedItem as ComboBoxItem;
            var tag = item == null ? "all" : (item.Tag as string ?? "all");

            var filtered = _allItems.Where(x => Matches(x, query, tag)).ToList();
            ProtectionList.ItemsSource = filtered;

            if (_allItems.Count == 0)
                EmptyHint.Text = "还没有保护项。点上面的「保护当前默认程序」，几秒钟就能完成。";
            else if (filtered.Count == 0)
                EmptyHint.Text = "没有匹配的保护项，试试其他关键词或筛选条件。";
            else
                EmptyHint.Text = string.Empty;

            PauseAllButton.IsEnabled = _serviceRunning && _allItems.Count > 0;
            ResumeAllButton.IsEnabled = _serviceRunning && _allItems.Any(i => i.Paused);
        }

        private static bool Matches(ProtectionItem item, string query, string tag)
        {
            if (query.Length > 0
                && !Contains(item.Title, query)
                && !Contains(item.Subtitle, query)
                && !Contains(item.ModuleText, query))
                return false;

            if (tag == "active")
                return item.Enabled && !item.Paused;
            if (tag == "paused")
                return item.Enabled && item.Paused;
            if (tag == "disabled")
                return !item.Enabled;
            return true;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnStartService(object sender, RoutedEventArgs e)
        {
            ServiceConnector.TryStart(_host);
        }

        private async void OnProtectCurrent(object sender, RoutedEventArgs e)
        {
            if (!_serviceRunning)
                return;

            var window = new AdoptDefaultsWindow(CurrentRules())
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules, "已保护当前默认程序。");
        }

        private async void OnAddDefaultApp(object sender, RoutedEventArgs e)
        {
            await AddViaEditor(RuleModule.DefaultApp);
        }

        private async void OnAddFileType(object sender, RoutedEventArgs e)
        {
            if (!_serviceRunning)
                return;

            var window = new ProtectFileTypeWindow { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules, "已保护这个文件类型。");
        }

        private async void OnAddNameSpace(object sender, RoutedEventArgs e)
        {
            await AddViaEditor(RuleModule.NameSpace);
        }

        private async void OnAddCustom(object sender, RoutedEventArgs e)
        {
            await AddViaEditor(RuleModule.CustomRegistry);
        }

        private async System.Threading.Tasks.Task AddViaEditor(RuleModule module)
        {
            if (!_serviceRunning)
                return;

            var window = new RuleEditorWindow(_host, module)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules, "已添加保护。");
        }

        private async void OnToggle(object sender, RoutedEventArgs e)
        {
            var check = sender as CheckBox;
            var item = check == null ? null : check.DataContext as ProtectionItem;
            if (item == null)
                return;

            var enabled = check.IsChecked == true;
            foreach (var rule in item.Rules)
                rule.Enabled = enabled;

            await SaveRules(item.Rules, enabled ? "已启用保护。" : "已停用保护。", false);
        }

        private void OnMore(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button == null || button.ContextMenu == null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.DataContext = button.DataContext;
            button.ContextMenu.IsOpen = true;
        }

        private async void OnEdit(object sender, RoutedEventArgs e)
        {
            var item = GetItem(sender);
            if (item == null || item.Rules.Count == 0)
                return;

            var window = new RuleEditorWindow(_host, item.Module, item.Rules[0])
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules, "已更新保护。");
        }

        private async void OnPauseToggle(object sender, RoutedEventArgs e)
        {
            var item = GetItem(sender);
            if (item == null)
                return;

            if (item.Paused)
            {
                foreach (var rule in item.Rules)
                    rule.PausedUntilUtc = null;
                await SaveRules(item.Rules, "已恢复保护。", false);
            }
            else
            {
                var until = DateTime.UtcNow.AddHours(1);
                foreach (var rule in item.Rules)
                    rule.PausedUntilUtc = until;
                await SaveRules(item.Rules, "已暂停 1 小时，到时会自动恢复。", false);
            }
        }

        private async void OnPauseAll(object sender, RoutedEventArgs e)
        {
            if (_allItems.Count == 0)
                return;

            var until = DateTime.UtcNow.AddHours(1);
            var rules = new List<RuleBase>();
            foreach (var item in _allItems)
            {
                foreach (var rule in item.Rules)
                {
                    if (!rule.Enabled)
                        continue;
                    rule.PausedUntilUtc = until;
                    rules.Add(rule);
                }
            }

            if (rules.Count == 0)
                return;

            await SaveRules(rules, "已暂停全部保护 1 小时，到时会自动恢复。", false);
        }

        private async void OnResumeAll(object sender, RoutedEventArgs e)
        {
            var rules = new List<RuleBase>();
            foreach (var item in _allItems)
            {
                foreach (var rule in item.Rules)
                {
                    if (!rule.PausedUntilUtc.HasValue)
                        continue;
                    rule.PausedUntilUtc = null;
                    rules.Add(rule);
                }
            }

            if (rules.Count == 0)
                return;

            await SaveRules(rules, "已恢复全部保护。", false);
        }

        private async void OnStop(object sender, RoutedEventArgs e)
        {
            var item = GetItem(sender);
            if (item == null)
                return;

            if (!_host.Confirm("停止保护「" + item.Title + "」？\n之后其他程序修改它时不会再被改回。"))
                return;

            var failed = new List<string>();
            foreach (var rule in item.Rules)
            {
                var response = await _host.SendAsync(IpcProtocol.CommandDeleteRule, rule.Id.ToString());
                if (!response.Success)
                    failed.Add(rule.Name + ": " + response.Error);
            }

            if (failed.Count > 0)
                _host.ShowWarning("有保护项没能停止：\n" + string.Join("\n", failed.Take(5)));
            else
                _host.ShowInfo("已停止保护。");

            _host.Refresh();
        }

        private ProtectionItem GetItem(object sender)
        {
            var element = sender as FrameworkElement;
            return element == null ? null : element.DataContext as ProtectionItem;
        }

        private List<RuleBase> CurrentRules()
        {
            var list = new List<RuleBase>();
            foreach (var item in _allItems)
                list.AddRange(item.Rules);
            return list;
        }

        private async System.Threading.Tasks.Task SaveRules(
            IEnumerable<RuleBase> rules, string successMessage, bool refresh = true)
        {
            var succeeded = 0;
            var errors = new List<string>();
            foreach (var rule in rules)
            {
                var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
                if (response.Success)
                    succeeded++;
                else
                    errors.Add((rule.Name ?? "保护项") + ": " + response.Error);
            }

            if (refresh)
                _host.Refresh();

            if (errors.Count > 0)
                _host.ShowWarning("保存完成：成功 " + succeeded + " 项，失败 " + errors.Count + " 项。\n"
                    + string.Join("\n", errors.Take(5)));
            else if (!string.IsNullOrEmpty(successMessage))
                _host.ShowInfo(successMessage);
        }
    }
}
