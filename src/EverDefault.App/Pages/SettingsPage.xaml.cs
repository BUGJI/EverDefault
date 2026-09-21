using System;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    public partial class SettingsPage : UserControl, IAppPage
    {
        private readonly IAppHost _host;
        private bool _loaded;
        private bool _loading;
        private bool _installed;
        private bool _installedKnown;
        private bool _serviceRunning;
        private ServiceHostMode _hostMode;

        public SettingsPage(IAppHost host)
        {
            _host = host;
            InitializeComponent();

            ThemeBox.ItemsSource = Display.Options<ThemeMode>(Display.Theme);
            OsBox.ItemsSource = Display.Options<OsFamily>(Display.Os);
            UserScopeBox.ItemsSource = Display.Options<UserScopeMode>(Display.UserScope);
            UpdateIntervalBox.ItemsSource = Display.Options<UpdateInterval>(Display.UpdateIntervalText);

            StartupBox.Checked += (s, e) => HideToTrayBox.IsEnabled = true;
            StartupBox.Unchecked += (s, e) => HideToTrayBox.IsEnabled = false;

            SaveButton.IsEnabled = false;
        }

        public void OnActivated()
        {
            RefreshModeSection(forceCheck: true);

            if (!_loaded)
                LoadAsync();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;
            if (_serviceRunning && snapshot.Status != null)
                _hostMode = snapshot.Status.HostMode;

            UpdateModeText();

            if (!snapshot.ServiceRunning)
            {
                Hint.Text = "后台服务未运行，无法读取或保存设置。请到“主页”启动服务。";
                SaveButton.IsEnabled = false;
                return;
            }

            Hint.Text = "这些设置保存在后台服务中，修改后点“保存设置”。";
            SaveButton.IsEnabled = _loaded;

            if (!_loaded)
                LoadAsync();
        }

        private void RefreshModeSection(bool forceCheck = false)
        {
            if (forceCheck || !_installedKnown)
            {
                _installed = ServiceControl.IsInstalled();
                _installedKnown = true;
            }

            InstallServiceButton.IsEnabled = !_installed;
            RestartServiceButton.IsEnabled = true;
            UninstallServiceButton.IsEnabled = _installed;
            UpdateModeText();
        }

        private void UpdateModeText()
        {
            if (_serviceRunning)
            {
                ModeStatusText.Text = _hostMode == ServiceHostMode.Service
                    ? "当前：服务模式（运行中）"
                    : "当前：用户模式（运行中）";
            }
            else
            {
                ModeStatusText.Text = _installed ? "当前：服务模式（未运行）" : "当前：用户模式（未运行）";
            }
        }

        private void OnInstallService(object sender, RoutedEventArgs e)
        {
            if (!_host.Confirm(
                    "将安装为 Windows 服务：开机自启，可守护系统级(HKLM)与所有用户。\n需要管理员权限，确定继续？"))
                return;

            if (ServiceControl.InstallAsService())
            {
                _installedKnown = false;
                ModeActionStatus.Text = "已启动安装程序，完成后将以服务模式运行。";
                ScheduleModeRefresh();
                _host.Refresh();
            }
            else
            {
                ModeActionStatus.Text = "未找到 install-service.cmd，或已取消授权。";
            }
        }

        private void OnRestartService(object sender, RoutedEventArgs e)
        {
            if (_installed)
            {
                ModeActionStatus.Text = ServiceControl.RestartInstalled()
                    ? "已请求重启服务（需要管理员权限）。"
                    : "重启被取消或失败。";
            }
            else
            {
                ModeActionStatus.Text = ServiceControl.RestartTemporary()
                    ? "已重启用户模式服务。"
                    : "未找到 EverDefault.Service.exe（应与本程序在同一目录）。";
                ScheduleReconnect();
            }

            _host.Refresh();
        }

        private void ScheduleReconnect()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                _host.Refresh();
            };
            timer.Start();
        }

        private void OnUninstallService(object sender, RoutedEventArgs e)
        {
            if (!_host.Confirm("确定卸载 Windows 服务？\n卸载后将回退为用户模式（随登录运行，仅守护当前用户）。"))
                return;

            if (ServiceControl.UninstallService())
            {
                _installedKnown = false;
                ModeActionStatus.Text = "已请求卸载服务；完成后将以用户模式运行。";
                ScheduleModeRefresh();
                StartTemporaryAfterDelay();
                _host.Refresh();
            }
            else
            {
                ModeActionStatus.Text = "未找到 uninstall-service.cmd，或已取消授权。";
            }
        }

        /// <summary>Re-checks installed state now and once more shortly after (the script runs async).</summary>
        private void ScheduleModeRefresh()
        {
            RefreshModeSection(forceCheck: true);

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                RefreshModeSection(forceCheck: true);
                _host.Refresh();
            };
            timer.Start();
        }

        private void StartTemporaryAfterDelay()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                if (!_host.ServiceRunning)
                    ServiceControl.StartTemporary();
            };
            timer.Start();
        }

        private async void LoadAsync()
        {
            if (_loading)
                return;

            _loading = true;
            IpcResponse response;
            try
            {
                response = await _host.SendAsync(IpcProtocol.CommandGetSettings);
            }
            finally
            {
                _loading = false;
            }

            if (!response.Success)
            {
                StatusText.Text = "服务未运行，无法读取设置：" + response.Error;
                return;
            }

            AppSettings settings;
            try
            {
                settings = PipeJson.Deserialize<AppSettings>(response.Payload) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                StatusText.Text = "设置解析失败：" + ex.Message;
                return;
            }

            ApplyToUi(settings);

            _loaded = true;
            SaveButton.IsEnabled = true;
            StatusText.Text = "已载入当前设置。";

            _host.ApplyUserSettings(settings);
        }

        private void ApplyToUi(AppSettings settings)
        {
            MonitoringBox.IsChecked = settings.MonitoringEnabled;
            StartupBox.IsChecked = settings.RunAtStartup;
            HideToTrayBox.IsChecked = settings.HideToTrayOnStartup;
            HideToTrayBox.IsEnabled = settings.RunAtStartup;
            ThemeBox.SelectedValue = settings.Theme;
            OsBox.SelectedValue = settings.OsOverride;
            RetentionBox.Text = settings.LogRetentionDays.ToString();
            UserScopeBox.SelectedValue = settings.TargetUserScope;
            CheckUpdatesBox.IsChecked = settings.CheckForUpdates;
            UpdateIntervalBox.SelectedValue = settings.UpdateCheckInterval;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            int retention;
            if (!int.TryParse(RetentionBox.Text, out retention) || retention <= 0)
                retention = 30;

            var settings = new AppSettings
            {
                MonitoringEnabled = MonitoringBox.IsChecked == true,
                RunAtStartup = StartupBox.IsChecked == true,
                HideToTrayOnStartup = HideToTrayBox.IsChecked == true,
                Theme = (ThemeMode)ThemeBox.SelectedValue,
                OsOverride = (OsFamily)OsBox.SelectedValue,
                LogRetentionDays = retention,
                TargetUserScope = (UserScopeMode)UserScopeBox.SelectedValue,
                CheckForUpdates = CheckUpdatesBox.IsChecked == true,
                UpdateCheckInterval = (UpdateInterval)UpdateIntervalBox.SelectedValue
            };

            var response = await _host.SendAsync(IpcProtocol.CommandSaveSettings, settings);
            if (!response.Success)
            {
                StatusText.Text = "服务未运行，设置未保存：" + response.Error;
                return;
            }

            StatusText.Text = "已保存设置。";
            _host.ApplyUserSettings(settings);
            _host.Refresh();
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            LoadAsync();
        }
    }
}
