using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EverDefault.App.Tray;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public partial class App : Application
    {
        private TrayIconHost _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

        /// <summary>Catches exceptions escaping async void handlers and other UI-thread work.</summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ReportUnhandled(e.Exception);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            ReportUnhandled(e.Exception);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportUnhandled(e.ExceptionObject as Exception);
        }

        private static void ReportUnhandled(Exception exception)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine("[EverDefault] unhandled: " + exception);
            }
            catch (Exception)
            {
            }

            var message = exception == null ? "未知错误" : exception.Message;
            MessageBox.Show(
                "EverDefault 遇到未处理的错误：\n\n" + message,
                "EverDefault", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
