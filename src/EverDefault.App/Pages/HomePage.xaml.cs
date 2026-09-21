using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EverDefault.Ipc;

namespace EverDefault.App
{
    public partial class HomePage : UserControl, IAppPage
    {
        private static readonly Brush OnlineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B)));
        private static readonly Brush OfflineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20)));

        private readonly IAppHost _host;

        private bool _running;
        private bool _installed;
        private bool _installedKnown;
        private ServiceHostMode _hostMode;

        public HomePage(IAppHost host)
        {
            _host = host;
            InitializeComponent();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            var wasRunning = _running;
            _running = snapshot.ServiceRunning;

            // Re-resolve installed state if the service just went away (e.g. uninstalled).
            if (wasRunning && !_running)
                _installedKnown = false;

            if (_running && snapshot.Status != null)
            {
                _hostMode = snapshot.Status.HostMode;
                StatusTitle.Text = "服务已连接";
                StatusTitle.Foreground = OnlineBrush;
                StatusDetail.Text = string.Format(
                    "{0} | 版本 {1} | 监听 {2} | 规则 {3} | 启动 {4:HH:mm:ss}",
                    snapshot.Status.OsDescription, snapshot.Status.Version,
                    snapshot.Status.ActiveWatchers, snapshot.Status.RuleCount,
                    snapshot.Status.StartedUtc.ToLocalTime());

                ObservedText.Text = snapshot.Status.ObservedCount.ToString();
                BlockedText.Text = snapshot.Status.BlockedCount.ToString();
                FailedText.Text = snapshot.Status.FailedCount.ToString();
            }
            else
            {
                if (!_installedKnown)
                {
                    _installed = ServiceControl.IsInstalled();
                    _installedKnown = true;
                }

                StatusTitle.Text = "服务未运行";
                StatusTitle.Foreground = OfflineBrush;
                StatusDetail.Text =
                    "监控与还原由后台服务完成，界面本身不会改动注册表。" +
                    (string.IsNullOrEmpty(snapshot.StatusError) ? string.Empty : "\n" + snapshot.StatusError);
            }

            ModeLabel.Text = ModeText();

            ConflictText.Text = snapshot.Conflicts != null && snapshot.Conflicts.Count > 0
                ? "冲突规则（不会执行，请修改）：\n" + string.Join("\n", snapshot.Conflicts)
                : string.Empty;
        }

        private string ModeText()
        {
            if (_running)
                return _hostMode == ServiceHostMode.Service ? "服务模式" : "用户模式";

            return _installed ? "服务模式" : "用户模式";
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
