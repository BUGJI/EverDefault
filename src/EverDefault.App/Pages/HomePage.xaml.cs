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
            _running = snapshot.ServiceRunning;

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

            ModeButton.Content = ModeText();

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

        private void OnModeClick(object sender, RoutedEventArgs e)
        {
            if (_running && _hostMode == ServiceHostMode.Service)
            {
                _host.ShowInfo("当前已在服务模式下运行。");
                return;
            }

            if (_running && _hostMode == ServiceHostMode.User)
            {
                UpgradeToService();
                return;
            }

            if (_installed)
            {
                if (ServiceControl.StartInstalledElevated())
                    ActionStatus.Text = "已请求以管理员权限启动已安装的服务，请稍候刷新。";
                else
                    ActionStatus.Text = "启动被取消或失败（需要管理员权限）。";
            }
            else
            {
                ActionStatus.Text = ServiceControl.StartTemporary()
                    ? "已以用户模式临时启动服务。"
                    : "未找到 EverDefault.Service.exe（应与本程序在同一目录）。";
            }

            _host.Refresh();
        }

        private void UpgradeToService()
        {
            if (!_host.Confirm(
                    "将停止用户模式，安装为 Windows 服务，并以服务模式重启。\n需要管理员权限，确定继续？"))
                return;

            if (ServiceControl.InstallAsService())
            {
                _installedKnown = false;
                ActionStatus.Text = "已启动安装程序，完成后将自动以服务模式运行。";
                _host.Refresh();
            }
            else
            {
                ActionStatus.Text = "未找到 install-service.cmd，或已取消授权。";
            }
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
