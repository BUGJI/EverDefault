using System;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace EverDefault.App.Tray
{
    /// <summary>Tray icon with an Open/Exit context menu. Closing the window hides it.</summary>
    public sealed class TrayIconHost : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private readonly Window _window;
        private readonly Action _shutdown;

        public TrayIconHost(Window window, Action shutdown)
        {
            _window = window;
            _shutdown = shutdown;

            _icon = new WinForms.NotifyIcon
            {
                Icon = LoadAppIcon(),
                Visible = true,
                Text = "EverDefault"
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("打开", null, (s, e) => ShowWindow());
            menu.Items.Add("退出", null, (s, e) => Exit());
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => ShowWindow();

            _window.Closing += (s, e) =>
            {
                if (!_shutdownRequested)
                {
                    e.Cancel = true;
                    _window.Hide();
                }
            };
        }

        private bool _shutdownRequested;

        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/EverDefault.App;component/Assets/logo.ico");
                var resource = Application.GetResourceStream(uri);
                if (resource != null)
                    return new System.Drawing.Icon(resource.Stream);
            }
            catch (Exception)
            {
                // Fall back to a system icon below.
            }

            return System.Drawing.SystemIcons.Shield;
        }

        private void ShowWindow()
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        private void Exit()
        {
            _shutdownRequested = true;
            _shutdown?.Invoke();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
