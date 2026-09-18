using System.Windows;
using EverDefault.App.Tray;

namespace EverDefault.App
{
    public partial class App : Application
    {
        private TrayIconHost _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var window = new MainWindow();
            _tray = new TrayIconHost(window, Shutdown);
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
