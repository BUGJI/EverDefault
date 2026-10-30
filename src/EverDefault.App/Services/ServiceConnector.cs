using System.IO;

namespace EverDefault.App
{
    /// <summary>Starts the background protection and explains failures in user-facing terms.</summary>
    internal static class ServiceConnector
    {
        public static void TryStart(IAppHost host)
        {
            if (host == null)
                return;

            if (host.ServiceRunning)
            {
                host.Refresh();
                return;
            }

            ServiceControl.ClearError();

            var installed = ServiceControl.IsInstalled();
            var started = installed
                ? ServiceControl.TryStartInstalled()
                : ServiceControl.StartTemporary();

            if (!started && installed && File.Exists(ServiceControl.ServiceExePath)
                && host.Confirm("启动后台保护需要管理员权限，是否继续？"))
            {
                started = ServiceControl.StartInstalledElevated();
            }

            if (!started)
            {
                host.ShowWarning(ServiceControl.LastError
                                 ?? "无法启动后台保护，请重新安装 EverDefault。");
            }

            host.Refresh();
        }
    }
}
