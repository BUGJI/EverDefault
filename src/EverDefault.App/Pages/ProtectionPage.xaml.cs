using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private List<RuleRow> _lastRules;
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

            if (snapshot.Rules != null && !ReferenceEquals(snapshot.Rules, _lastRules))
            {
                _lastRules = snapshot.Rules;
                _allItems = ProtectionBuilder.BuildItems(snapshot.Rules.Select(r => r.Rule));
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
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            PauseAllButton.IsEnabled = _serviceRunning && _allItems.Count > 0;
            ResumeAllButton.IsEnabled = _serviceRunning && _allItems.Any(i => i.Paused);
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            var empty = string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
        }

        private void OnClearSearch(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            SearchBox.Focus();
        }

        public void FocusSearch()
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
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

            UpdateActionButtons();
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

            await SaveRules(window.Rules, "已保护当前默认程序。", apply: true);
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

            await SaveRules(window.Rules, "已保护这个文件类型。", apply: true);
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

            await SaveRules(window.Rules, "已添加保护。", apply: true);
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

            var saved = await SaveRules(item.Rules, enabled ? "已启用保护。" : "已停用保护。", false);
            if (saved)
                return;

            foreach (var rule in item.Rules)
                rule.Enabled = !enabled;
            item.Enabled = !enabled;
            check.IsChecked = !enabled;
            _host.Refresh();
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

            await SaveRules(window.Rules, "已更新保护。", apply: true);
        }

        private async void OnPauseItemFor(object sender, RoutedEventArgs e)
        {
            var item = GetItem(sender);
            if (item == null)
                return;

            var minutes = MinutesFrom(sender);
            var until = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : (DateTime?)null;
            foreach (var rule in item.Rules)
                rule.PausedUntilUtc = until;

            var message = minutes > 0
                ? "已暂停 " + DescribeDuration(minutes) + "，到时会自动恢复。"
                : "已恢复保护。";
            await SaveRules(item.Rules, message);
        }

        private async void OnPauseAll(object sender, RoutedEventArgs e)
        {
            await PauseAll(60);
        }

        private async void OnPauseAllFor(object sender, RoutedEventArgs e)
        {
            await PauseAll(MinutesFrom(sender));
        }

        private async System.Threading.Tasks.Task PauseAll(int minutes)
        {
            if (_allItems.Count == 0 || minutes <= 0)
                return;

            var until = DateTime.UtcNow.AddMinutes(minutes);
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

            await SaveRules(rules, "已暂停全部保护 " + DescribeDuration(minutes) + "，到时会自动恢复。");
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

            await SaveRules(rules, "已恢复全部保护。");
        }

        private static int MinutesFrom(object sender)
        {
            var menu = sender as MenuItem;
            var raw = menu == null ? null : menu.Tag as string;
            int minutes;
            return int.TryParse(raw, out minutes) ? minutes : 60;
        }

        private static string DescribeDuration(int minutes)
        {
            if (minutes == 15)
                return "15 分钟";
            if (minutes == 60)
                return "1 小时";
            if (minutes == 480)
                return "8 小时";
            return minutes + " 分钟";
        }

        private async void OnStop(object sender, RoutedEventArgs e)
        {
            var item = GetItem(sender);
            if (item == null)
                return;

            if (!_host.Confirm("停止保护「" + item.Title + "」？\n之后其他程序修改它时不会再被改回。"))
                return;

            var ids = item.Rules.Select(r => r.Id.ToString()).ToList();
            var response = await _host.SendAsync(IpcProtocol.CommandDeleteRules, ids);
            if (!response.Success)
                _host.ShowWarning("保护项没能停止：" + response.Error);
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

        private async System.Threading.Tasks.Task<bool> SaveRules(
            IEnumerable<RuleBase> rules, string successMessage, bool refresh = true, bool apply = false)
        {
            var list = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => r != null)
                .ToList();
            if (list.Count == 0)
                return true;

            if (apply)
            {
                var outcome = await RuleApply.SaveAndRunAsync(_host, list, successMessage);
                if (refresh)
                    _host.Refresh();

                return outcome.Saved;
            }

            var payload = list.Select(RuleCodec.Serialize).ToList();
            var response = await _host.SendAsync(IpcProtocol.CommandSaveRules, payload);

            if (refresh)
                _host.Refresh();

            if (!response.Success)
            {
                _host.ShowWarning("保存失败：" + response.Error);
                return false;
            }

            if (!string.IsNullOrEmpty(successMessage))
                _host.ShowInfo(successMessage);

            return true;
        }
    }
}
