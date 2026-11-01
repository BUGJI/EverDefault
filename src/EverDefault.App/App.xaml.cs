using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EverDefault.App.Tray;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public partial class App : Application
    {
        private const string MutexName = "EverDefault.App.SingleInstance";
        private const string WindowTitle = "EverDefault - 保护你的默认设置";

        private TrayIconHost _tray;
        private Mutex _instanceMutex;
        private bool _ownsMutex;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var startHidden = e.Args.Any(
                a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            _instanceMutex = new Mutex(false, MutexName);
            var acquired = false;
            try
            {
                acquired = _instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                if (!startHidden)
                    ActivateExistingWindow();

                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }

            _ownsMutex = true;

            ThemeManager.Initialize();
            ThemeManager.Apply(ThemeMode.System);
            ApplyLegacyIconLabels();

            var window = new MainWindow();
            _tray = new TrayIconHost(
                window,
                Shutdown,
                () => window.Dispatcher.Invoke(new Action(window.TogglePause)),
                id => window.Dispatcher.Invoke(new Action(() => window.OpenRuleById(id))),
                () => window.Dispatcher.Invoke(new Action(() => window.Navigate("activity"))),
                () => window.Dispatcher.Invoke(new Action(() => window.Navigate("settings"))),
                minutes => window.Dispatcher.Invoke(new Action(() => window.PauseFor(minutes))),
                () => (bool)window.Dispatcher.Invoke(new Func<bool>(window.ConfirmExit)));

            window.NotificationRequested += message =>
                _tray.ShowNotification(message.Title, message.Text, message.RuleId, message.IsFailure);
            window.ProtectionStateChanged += (text, state) => _tray.SetState(text, state);

            Task.Run(() => EnsureServiceStarted());

            if (!startHidden)
                window.Show();
        }

        /// <summary>On Windows 7/8 the MDL2 icon font is missing, so icon-only buttons get text.</summary>
        private void ApplyLegacyIconLabels()
        {
            if (IconFontSupport.IsAvailable)
                return;

            Resources["IconExportContent"] = "导出";
            Resources["IconImportContent"] = "导入";
            Resources["IconRefreshContent"] = "刷新";
            Resources["IconMoreContent"] = "更多";
            Resources["WindowMinimizeContent"] = "最小化";
            Resources["WindowMaximizeContent"] = "最大化";
            Resources["WindowCloseContent"] = "关闭";
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

        private static void ActivateExistingWindow()
        {
            var handle = FindWindow(null, WindowTitle);
            if (handle == IntPtr.Zero)
                return;

            ShowWindow(handle, 9);
            SetForegroundWindow(handle);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();

            if (_instanceMutex != null)
            {
                try
                {
                    if (_ownsMutex)
                        _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The mutex was already released.
                }

                _instanceMutex.Dispose();
                _instanceMutex = null;
            }

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
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "EverDefault");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "app-error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine
                    + exception + Environment.NewLine + Environment.NewLine);
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
