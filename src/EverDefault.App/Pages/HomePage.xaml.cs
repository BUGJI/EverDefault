using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EverDefault.Core.Model;
using EverDefault.Ipc;
using EverDefault.Registry.Associations;

namespace EverDefault.App
{
    public partial class HomePage : UserControl, IAppPage
    {
        private readonly IAppHost _host;
        private RefreshSnapshot _snapshot;
        private AppSettings _settings;

        public HomePage(IAppHost host)
        {
            _host = host;
            InitializeComponent();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _snapshot = snapshot;

            if (!snapshot.ServiceRunning)
            {
                if (snapshot.ServiceStarting)
                {
                    StatusTitle.Text = "正在启动后台保护…";
                    StatusDetail.Text = "首次启动或刚安装后需要几秒，请稍候。";
                    ReconnectButton.Visibility = Visibility.Collapsed;
                    SetTone("WarningSubtleBrush", "WarningBrush", "\uE769");
                }
                else
                {
                    StatusTitle.Text = "后台保护未运行";
                    StatusDetail.Text = "点击“重试连接”启动后台保护。";
                    ReconnectButton.Visibility = Visibility.Visible;
                    SetTone("DangerSubtleBrush", "DangerBrush", "\uE7BA");
                }

                PauseButton.IsEnabled = false;
                PauseButton.Content = "暂停 1 小时";
                ModeLabel.Text = "未连接";
                ObservedText.Text = "0";
                BlockedText.Text = "0";
                FailedText.Text = "0";
                RecentEmpty.Visibility = Visibility.Visible;
                RecentList.ItemsSource = null;
                ConflictCard.Visibility = Visibility.Collapsed;
                BrowserStateText.Text = "后台保护未运行";
                DocsStateText.Text = "后台保护未运行";
                NameSpaceStateText.Text = "后台保护未运行";
                SettingsStateText.Text = "后台保护未运行";
                return;
            }

            var status = snapshot.Status;
            var now = DateTime.UtcNow;
            var paused = status.PauseUntilUtc.HasValue && status.PauseUntilUtc.Value > now;
            var monitoring = status.MonitoringEnabled;
            var ruleCount = snapshot.Rules == null ? 0 : snapshot.Rules.Count;

            if (!monitoring)
            {
                StatusTitle.Text = "保护已关闭";
                StatusDetail.Text = "开启后才会继续守住这些设置。";
                SetTone("WarningSubtleBrush", "WarningBrush", "\uE7BA");
            }
            else if (paused)
            {
                StatusTitle.Text = "保护已暂停";
                StatusDetail.Text = "将在 " + status.PauseUntilUtc.Value.ToLocalTime().ToString("HH:mm")
                                    + " 自动恢复。";
                SetTone("WarningSubtleBrush", "WarningBrush", "\uE769");
            }
            else
            {
                StatusTitle.Text = "正在保护你的设置";
                StatusDetail.Text = ruleCount == 0
                    ? "还没有保护项，先从下面的推荐开始。"
                    : "共 " + ruleCount + " 条保护规则 · 有程序改动时会自动改回。";
                SetTone("SuccessSubtleBrush", "SuccessBrush", "\uE73E");
            }

            PauseButton.IsEnabled = true;
            PauseButton.Content = paused
                ? "立即恢复"
                : monitoring ? "暂停 1 小时" : "恢复保护";
            ReconnectButton.Visibility = Visibility.Collapsed;

            ModeLabel.Text = status.HostMode == ServiceHostMode.Service ? "服务模式" : "用户模式";

            ObservedText.Text = status.ObservedCount.ToString();
            BlockedText.Text = status.BlockedCount.ToString();
            FailedText.Text = status.FailedCount.ToString();

            var conflicts = snapshot.Conflicts ?? new List<string>();
            ConflictCard.Visibility = conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ConflictText.Text = "有 " + conflicts.Count + " 组保护设置互相冲突，这些规则暂时不会生效。";

            UpdateRecommendations(snapshot.Rules == null
                ? new List<RuleBase>()
                : snapshot.Rules.Select(r => r.Rule).ToList());

            var recent = (snapshot.Logs ?? new List<ActivityItem>()).Take(4).ToList();
            RecentList.ItemsSource = recent;
            RecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
        }

        private void SetTone(string backgroundKey, string foregroundKey, string glyph)
        {
            if (StatusTile != null)
                StatusTile.Background = TryFindResource(backgroundKey) as Brush ?? Brushes.Transparent;

            if (StatusGlyph != null)
            {
                StatusGlyph.Foreground = TryFindResource(foregroundKey) as Brush ?? Brushes.Gray;
                StatusGlyph.Text = glyph;
            }
        }

        private void OnReconnect(object sender, RoutedEventArgs e)
        {
            ServiceConnector.TryStart(_host);
        }

        private async void OnPause(object sender, RoutedEventArgs e)
        {
            if (_snapshot == null || !_snapshot.ServiceRunning || _snapshot.Status == null)
                return;

            var status = _snapshot.Status;
            var paused = status.PauseUntilUtc.HasValue && status.PauseUntilUtc.Value > DateTime.UtcNow;

            IpcResponse response;
            if (paused)
                response = await _host.SendAsync(IpcProtocol.CommandResumeAll);
            else if (!status.MonitoringEnabled)
                response = await _host.SendAsync(IpcProtocol.CommandSetMonitoring, true.ToString());
            else
                response = await _host.SendAsync(IpcProtocol.CommandPauseAll, 60);

            if (!response.Success)
                _host.ShowWarning("操作失败：" + response.Error);

            _host.Refresh();
        }

        private async void OnTemplate(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var tag = button == null ? null : button.Tag as string;

            if (tag == "thispc")
            {
                await OpenEditor(RuleModule.NameSpace);
                return;
            }

            if (tag == "settings")
            {
                await OpenEditor(RuleModule.CustomRegistry);
                return;
            }

            if (tag == "browser")
            {
                await ProtectCurrent(new List<string>(), new List<string> { "http", "https", "mailto" });
                return;
            }

            if (tag == "docs")
            {
                await ProtectCurrent(
                    new List<string> { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" },
                    new List<string>());
            }
        }

        private async System.Threading.Tasks.Task OpenEditor(RuleModule module)
        {
            if (_snapshot == null || !_snapshot.ServiceRunning)
            {
                _host.ShowInfo("后台服务未运行，暂时无法添加保护。");
                return;
            }

            var window = new RuleEditorWindow(_host, module)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            await SaveRules(window.Rules, "已添加保护。");
        }

        private async System.Threading.Tasks.Task ProtectCurrent(
            IList<string> extensions, IList<string> protocols)
        {
            if (_snapshot == null || !_snapshot.ServiceRunning)
            {
                _host.ShowInfo("后台服务未运行，暂时无法添加保护。");
                return;
            }

            var rules = ProtectionBuilder.BuildCurrentRules(extensions, protocols);
            if (rules.Count == 0)
            {
                _host.ShowInfo("没有找到当前默认程序，或这些项目已经在保护中。");
                return;
            }

            var existing = _snapshot.Rules == null
                ? new List<RuleBase>()
                : _snapshot.Rules.Select(r => r.Rule).ToList();

            var filtered = rules.Where(rule =>
                (rule.Extensions ?? new List<string>()).Any(ext => !IsCovered(existing, ext, false))
                || (rule.Protocols ?? new List<string>()).Any(p => !IsCovered(existing, p, true)))
                .ToList();

            if (filtered.Count == 0)
            {
                _host.ShowInfo("这些项目已经在保护中。");
                return;
            }

            await SaveRules(filtered, "已保护当前默认程序。");
        }

        private static bool IsCovered(IEnumerable<RuleBase> existing, string target, bool protocol)
        {
            foreach (var app in existing.OfType<DefaultAppRule>())
            {
                if (protocol)
                {
                    if (app.Protocols != null && app.Protocols.Any(p => string.Equals(
                        DefaultAppRule.NormalizeProtocol(p), DefaultAppRule.NormalizeProtocol(target),
                        StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
                else
                {
                    if (app.Extensions != null && app.Extensions.Any(x => string.Equals(
                        DefaultAppRule.NormalizeExtension(x), DefaultAppRule.NormalizeExtension(target),
                        StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            return false;
        }

        private async System.Threading.Tasks.Task SaveRules(IEnumerable<RuleBase> rules, string message)
        {
            var saved = 0;
            var errors = new List<string>();
            foreach (var rule in rules)
            {
                var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
                if (response.Success)
                    saved++;
                else
                    errors.Add((rule.Name ?? "保护项") + ": " + response.Error);
            }

            _host.Refresh();

            if (errors.Count > 0)
                _host.ShowWarning("保存完成：成功 " + saved + " 项，失败 " + errors.Count + " 项。\n"
                    + string.Join("\n", errors.Take(5)));
            else
                _host.ShowInfo(message);
        }

        private void UpdateRecommendations(IList<RuleBase> rules)
        {
            BrowserStateText.Text = DescribeAssociation(rules, "http", AssociationKind.UrlProtocol);
            DocsStateText.Text = DescribeAssociation(rules, ".pdf", AssociationKind.FileExtension);

            NameSpaceStateText.Text = rules.Any(r => r.Module == RuleModule.NameSpace)
                ? "已添加此电脑保护"
                : "尚未添加";

            SettingsStateText.Text = rules.Any(r => r.Module == RuleModule.CustomRegistry)
                ? "已添加系统设置保护"
                : "尚未添加";
        }

        private static string DescribeAssociation(
            IEnumerable<RuleBase> rules, string target, AssociationKind kind)
        {
            var entry = AssociationCatalog.Resolve(target, kind);
            if (entry == null || string.IsNullOrWhiteSpace(entry.AppName))
                return "尚未设置默认程序";

            return ProtectionBuilder.IsCovered(rules, entry)
                ? "当前：" + entry.AppName + " · 已保护"
                : "当前：" + entry.AppName;
        }

        private void OnOpenActivity(object sender, RoutedEventArgs e)
        {
            _host.ShowActivity("all");
        }

        private void OnStatClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            var tag = element == null ? null : element.Tag as string;
            _host.ShowActivity(tag ?? "all");
        }

        private void OnResolveConflicts(object sender, RoutedEventArgs e)
        {
            if (_snapshot == null || _snapshot.Rules == null)
                return;

            var window = new ConflictResolverWindow(
                _host,
                _snapshot.Rules.Select(r => r.Rule).ToList(),
                _snapshot.ConflictingRuleIds ?? new List<Guid>())
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
            _host.Refresh();
        }
    }
}
