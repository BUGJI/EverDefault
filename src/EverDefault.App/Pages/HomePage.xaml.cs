using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EverDefault.App
{
    public partial class HomePage : UserControl, IAppPage
    {
        private const string ServiceName = "EverDefault";

        private static readonly Brush OnlineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B)));
        private static readonly Brush OfflineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20)));

        private readonly IAppHost _host;

        public HomePage(IAppHost host)
        {
            _host = host;
            InitializeComponent();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            if (!snapshot.ServiceRunning)
            {
                StatusTitle.Text = "服务未运行";
                StatusTitle.Foreground = OfflineBrush;
                StatusDetail.Text =
                    "监控与还原由后台服务完成，界面本身不会改动注册表。请选择下面的启动方式。" +
                    (string.IsNullOrEmpty(snapshot.StatusError) ? string.Empty : "\n" + snapshot.StatusError);
            }
            else
            {
                var status = snapshot.Status;
                StatusTitle.Text = "服务已连接";
                StatusTitle.Foreground = OnlineBrush;
                StatusDetail.Text = string.Format(
                    "{0} | 版本 {1} | 监听 {2} | 规则 {3} | 启动 {4:HH:mm:ss}",
                    status.OsDescription, status.Version, status.ActiveWatchers, status.RuleCount,
                    status.StartedUtc.ToLocalTime());
            }

            ConflictText.Text = snapshot.Conflicts != null && snapshot.Conflicts.Count > 0
                ? "冲突规则（不会执行，请修改）：\n" + string.Join("\n", snapshot.Conflicts)
                : string.Empty;
        }

        private void OnStartTemporary(object sender, RoutedEventArgs e)
        {
            var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EverDefault.Service.exe");
            if (!File.Exists(exe))
            {
                ActionStatus.Text = "未找到 EverDefault.Service.exe（应与本程序在同一目录）。";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exe, "--console") { UseShellExecute = true });
                ActionStatus.Text = "已启动临时服务（控制台窗口）。关闭该控制台窗口即停止监控。";
                _host.Refresh();
            }
            catch (Exception ex)
            {
                _host.ShowWarning("启动失败：" + ex.Message);
            }
        }

        private void OnInstall(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "install-service.cmd");
            if (!File.Exists(path))
            {
                ActionStatus.Text = "未找到 install-service.cmd（应与本程序在同一目录）。";
                return;
            }

            RunElevated(path, null, "安装脚本");
        }

        private void OnStartInstalled(object sender, RoutedEventArgs e)
        {
            RunElevated("cmd.exe", "/c sc start " + ServiceName, "启动命令");
        }

        private void RunElevated(string fileName, string arguments, string label)
        {
            try
            {
                var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = true, Verb = "runas" };
                if (!string.IsNullOrEmpty(arguments))
                    startInfo.Arguments = arguments;

                Process.Start(startInfo);
                ActionStatus.Text = "已打开" + label + "。完成后稍等几秒查看上方状态。";
                _host.Refresh();
            }
            catch (Win32Exception)
            {
                ActionStatus.Text = "已取消系统授权。安装或启动服务需要管理员权限。";
            }
            catch (Exception ex)
            {
                _host.ShowWarning(label + "失败：" + ex.Message);
            }
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
