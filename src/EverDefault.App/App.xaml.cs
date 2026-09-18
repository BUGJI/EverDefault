using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using EverDefault.App.Tray;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public partial class App : Application
    {
        private TrayIconHost _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ThemeManager.Apply(ThemeMode.System);

            var startHidden = e.Args.Any(
                a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            var window = new MainWindow();
            _tray = new TrayIconHost(window, Shutdown);

            Task.Run(() => EnsureServiceStarted());

            if (!startHidden)
                window.Show();
        }

        /// <summary>Auto-connects on launch: start the installed service, else a temporary engine.</summary>
        private static void EnsureServiceStarted()
        {
            var client = new EverDefault.Ipc.PipeClient();
            if (client.IsServiceRunning())
                return;

            if (ServiceControl.IsInstalled())
                ServiceControl.TryStartInstalled();
            else
                ServiceControl.StartTemporary();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
