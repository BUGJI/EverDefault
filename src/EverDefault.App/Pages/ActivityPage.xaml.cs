using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    /// <summary>A plain-language history of blocked changes.</summary>
    public partial class ActivityPage : UserControl, IAppPage
    {
        private readonly IAppHost _host;
        private List<ActivityItem> _items = new List<ActivityItem>();
        private Dictionary<Guid, RuleBase> _rules = new Dictionary<Guid, RuleBase>();
        private List<RuleRow> _lastRules;
        private string _filter = "all";

        public ActivityPage(IAppHost host)
        {
            _host = host;
            InitializeComponent();
            UpdateFilterButtons();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            var logsChanged = snapshot.Logs != null && !ReferenceEquals(snapshot.Logs, _items);
            if (logsChanged)
                _items = snapshot.Logs;

            if (snapshot.Rules != null && !ReferenceEquals(snapshot.Rules, _lastRules))
            {
                _lastRules = snapshot.Rules;
                _rules = snapshot.Rules
                    .GroupBy(r => r.Rule.Id)
                    .ToDictionary(g => g.Key, g => g.First().Rule);
            }

            ClearButton.IsEnabled = snapshot.ServiceRunning;
            SearchBox.IsEnabled = snapshot.ServiceRunning;
            ClearSearchButton.IsEnabled = snapshot.ServiceRunning;
            Hint.Text = snapshot.ServiceRunning
                ? "EverDefault 每次改回设置都会记在这里。"
                : "后台服务未运行，以下为上次读取到的记录。";

            if (logsChanged || ActivityList.ItemsSource == null)
                ApplyFilter();
        }

        public void SetFilter(string filter)
        {
            _filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter;
            UpdateFilterButtons();
            ApplyFilter();
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

        private async void OnClear(object sender, RoutedEventArgs e)
        {
            if (!_host.Confirm("清空全部活动记录？\n这不会影响正在生效的保护规则。"))
                return;

            var response = await _host.SendAsync(IpcProtocol.CommandClearLog);
            if (!response.Success)
            {
                _host.ShowWarning("清空失败：" + response.Error);
                return;
            }

            _items = new List<ActivityItem>();
            ActivityList.ItemsSource = null;
            EmptyHint.Text = "还没有活动。保护生效后，这里会显示每一次拦截。";
            _host.ShowInfo("已清空活动记录。");
            _host.Refresh();
        }

        public void Highlight(Guid? ruleId)
        {
            if (!ruleId.HasValue)
                return;

            var item = _items.FirstOrDefault(i => i.RuleId == ruleId.Value);
            if (item == null)
                return;

            _filter = "all";
            UpdateFilterButtons();
            SearchBox.Text = string.Empty;

            var reordered = new List<ActivityItem> { item };
            reordered.AddRange(_items.Where(i => !ReferenceEquals(i, item)));
            ActivityList.ItemsSource = reordered;
            EmptyHint.Text = string.Empty;
            ActivityList.BringIntoView();
        }

        private void OnFilter(object sender, RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            _filter = button == null ? "all" : (button.Tag as string ?? "all");
            UpdateFilterButtons();
            ApplyFilter();
        }

        private void UpdateFilterButtons()
        {
            StyleButton(AllButton, _filter == "all");
            StyleButton(BlockedButton, _filter == "blocked");
            StyleButton(FailedButton, _filter == "failed");
        }

        private static void StyleButton(ToggleButton button, bool selected)
        {
            button.IsChecked = selected;
        }

        private void ApplyFilter()
        {
            IEnumerable<ActivityItem> filtered = _items;
            if (_filter == "blocked")
                filtered = filtered.Where(i => i.IsBlocked);
            else if (_filter == "failed")
                filtered = filtered.Where(i => i.IsFailed);

            var query = (SearchBox.Text ?? string.Empty).Trim();
            if (query.Length > 0)
            {
                filtered = filtered.Where(i =>
                    Contains(i.Title, query)
                    || Contains(i.Detail, query)
                    || Contains(i.RuleName, query)
                    || Contains(i.RawDetail, query));
            }

            var list = filtered.ToList();
            ActivityList.ItemsSource = list;

            EmptyHint.Text = list.Count == 0
                ? (_filter == "all" && query.Length == 0
                    ? "还没有活动。保护生效后，这里会显示每一次拦截。"
                    : "没有匹配的活动记录，试试其他关键词或筛选条件。")
                : string.Empty;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async void OnAdjust(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button == null ? null : button.DataContext as ActivityItem;
            if (item == null || !item.RuleId.HasValue)
                return;

            RuleBase rule;
            if (!_rules.TryGetValue(item.RuleId.Value, out rule))
                return;

            var window = new RuleEditorWindow(_host, rule.Module, rule)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            var outcome = await RuleApply.SaveAndRunAsync(_host, window.Rules, "已更新保护。");
            if (!outcome.Saved)
                return;

            _host.Refresh();
        }
    }
}
