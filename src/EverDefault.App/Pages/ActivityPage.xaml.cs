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
        private string _filter = "all";

        public ActivityPage(IAppHost host)
        {
            _host = host;
            InitializeComponent();
            UpdateFilterButtons();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _items = snapshot.Logs ?? new List<ActivityItem>();
            _rules = snapshot.Rules == null
                ? new Dictionary<Guid, RuleBase>()
                : snapshot.Rules
                    .GroupBy(r => r.Rule.Id)
                    .ToDictionary(g => g.Key, g => g.First().Rule);

            OfflineBanner.Visibility = snapshot.ServiceRunning
                ? Visibility.Collapsed
                : Visibility.Visible;
            OfflineText.Text = snapshot.ServiceStarting
                ? "正在启动后台保护，请稍候…"
                : "后台保护未运行，暂时没有活动记录。";
            ClearButton.IsEnabled = snapshot.ServiceRunning;
            SearchBox.IsEnabled = snapshot.ServiceRunning;
            Hint.Text = snapshot.ServiceRunning
                ? "EverDefault 每次改回设置都会记在这里。"
                : "后台服务未运行，暂时没有活动记录。";

            ApplyFilter();
        }

        public void SetFilter(string filter)
        {
            _filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter;
            UpdateFilterButtons();
            ApplyFilter();
        }

        private void OnStartService(object sender, RoutedEventArgs e)
        {
            ServiceConnector.TryStart(_host);
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyFilter();
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

            foreach (var updated in window.Rules)
            {
                var response = await _host.SendAsync(
                    IpcProtocol.CommandSaveRule, RuleCodec.Serialize(updated));
                if (!response.Success)
                {
                    _host.ShowWarning("保存失败：" + response.Error);
                    return;
                }
            }

            _host.ShowInfo("已更新保护。");
            _host.Refresh();
        }
    }
}
