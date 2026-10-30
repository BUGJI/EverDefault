using System;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace EverDefault.App.Tray
{
    public enum TrayProtectionState
    {
        Unavailable,
        Disabled,
        Paused,
        Active
    }

    /// <summary>Tray icon with balloons for blocked changes and a pause shortcut.</summary>
    public sealed class TrayIconHost : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private readonly Window _window;
        private readonly Action _shutdown;
        private readonly Action _togglePause;
        private readonly Action<Guid?> _notificationActivated;
        private readonly Action _openActivity;
        private readonly Action _openSettings;
        private readonly WinForms.ToolStripMenuItem _pauseItem;

        private bool _shutdownRequested;
        private bool _closeHintShown;
        private Guid? _lastRuleId;

        public TrayIconHost(
            Window window,
            Action shutdown,
            Action togglePause,
            Action<Guid?> notificationActivated,
            Action openActivity = null,
            Action openSettings = null)
        {
            _window = window;
            _shutdown = shutdown;
            _togglePause = togglePause;
            _notificationActivated = notificationActivated;
            _openActivity = openActivity;
            _openSettings = openSettings;

            _icon = new WinForms.NotifyIcon
            {
                Icon = LoadAppIcon(),
                Visible = true,
                Text = "EverDefault - 正在保护你的设置"
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("打开", null, (s, e) => ShowWindow());

            _pauseItem = new WinForms.ToolStripMenuItem("暂停保护 1 小时");
            _pauseItem.Click += (s, e) =>
            {
                var handler = _togglePause;
                if (handler != null)
                    handler();
            };
            menu.Items.Add(_pauseItem);

            menu.Items.Add("查看活动", null, (s, e) =>
            {
                ShowWindow();
                var handler = _openActivity;
                if (handler != null)
                    handler();
            });
            menu.Items.Add("设置", null, (s, e) =>
            {
                ShowWindow();
                var handler = _openSettings;
                if (handler != null)
                    handler();
            });

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => Exit());

            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => ShowWindow();
            _icon.BalloonTipClicked += (s, e) =>
            {
                ShowWindow();
                var handler = _notificationActivated;
                if (handler != null)
                    handler(_lastRuleId);
            };

            _window.Closing += (s, e) =>
            {
                if (_shutdownRequested)
                    return;

                e.Cancel = true;
                _window.Hide();

                if (!_closeHintShown)
                {
                    _closeHintShown = true;
                    ShowNotification(
                        "EverDefault 仍在后台保护",
                        "窗口已最小化到系统托盘。双击托盘图标可重新打开，右键可暂停或退出。",
                        null,
                        false);
                }
            };
        }

        public void SetState(string text, TrayProtectionState state)
        {
            var tooltip = string.IsNullOrWhiteSpace(text) ? "EverDefault" : "EverDefault - " + text;
            _icon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;

            _pauseItem.Enabled = state != TrayProtectionState.Unavailable;
            _pauseItem.Text = state == TrayProtectionState.Paused
                ? "恢复保护"
                : state == TrayProtectionState.Disabled
                    ? "启用保护"
                    : "暂停保护 1 小时";
        }

        public void ShowNotification(string title, string text, Guid? ruleId, bool failure)
        {
            if (ruleId.HasValue)
                _lastRuleId = ruleId;

            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = failure
                ? WinForms.ToolTipIcon.Warning
                : WinForms.ToolTipIcon.Info;
            _icon.ShowBalloonTip(6000);
        }

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
