using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
        private bool _dirty;
        private bool _suppressDirty;
        private ServiceHostMode _hostMode;
        private AppSettings _current;

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

            MonitoringBox.Checked += OnSettingChanged;
            MonitoringBox.Unchecked += OnSettingChanged;
            StartupBox.Checked += OnSettingChanged;
            StartupBox.Unchecked += OnSettingChanged;
            HideToTrayBox.Checked += OnSettingChanged;
            HideToTrayBox.Unchecked += OnSettingChanged;
            NotificationsBox.Checked += OnSettingChanged;
            NotificationsBox.Unchecked += OnSettingChanged;
            AdvancedModeBox.Checked += OnSettingChanged;
            AdvancedModeBox.Unchecked += OnSettingChanged;
            CheckUpdatesBox.Checked += OnSettingChanged;
            CheckUpdatesBox.Unchecked += OnSettingChanged;
            ThemeBox.SelectionChanged += OnSettingChanged;
            ThemeBox.SelectionChanged += OnThemePreview;
            UpdateIntervalBox.SelectionChanged += OnSettingChanged;
            OsBox.SelectionChanged += OnSettingChanged;
            UserScopeBox.SelectionChanged += OnSettingChanged;
            RetentionBox.TextChanged += OnSettingChanged;

            CurrentVersionText.Text = "当前版本 " + UpdateChecker.CurrentVersion();
            SaveButton.IsEnabled = false;
        }

        public void OnActivated()
        {
            _ = RefreshModeSectionAsync(forceCheck: true);

            if (!_loaded)
                LoadAsync();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;
            if (_serviceRunning && snapshot.Status != null)
                _hostMode = snapshot.Status.HostMode;

            UpdateModeText();

            ClearLogButton.IsEnabled = _serviceRunning;

            if (!snapshot.ServiceRunning)
            {
                Hint.Text = "后台服务未运行，暂时无法读取或保存设置。";
                SaveButton.IsEnabled = false;
                return;
            }

            Hint.Text = "这些设置保存在后台服务中，修改后点“保存设置”。";
            SaveButton.IsEnabled = _loaded;

            if (!_loaded)
                LoadAsync();
        }

        public bool HasUnsavedChanges
        {
            get { return _dirty; }
        }

        private void OnThemePreview(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDirty || !_loaded)
                return;

            if (ThemeBox.SelectedValue is ThemeMode)
                ThemeManager.Apply((ThemeMode)ThemeBox.SelectedValue);
        }

        private async Task RefreshModeSectionAsync(bool forceCheck = false)
        {
            if (forceCheck || !_installedKnown)
            {
                _installed = await Task.Run(() => ServiceControl.IsInstalled());
                _installedKnown = true;
            }

            UpdateModeButtons();
        }

        private void RefreshModeSection(bool forceCheck = false)
        {
            if (forceCheck || !_installedKnown)
            {
                _installed = ServiceControl.IsInstalled();
                _installedKnown = true;
            }

            UpdateModeButtons();
        }

        private void UpdateModeButtons()
        {
            InstallServiceButton.IsEnabled = !_installed;
            RestartServiceButton.IsEnabled = true;
            UninstallServiceButton.IsEnabled = _installed;
            UpdateModeText();
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (_suppressDirty || !_loaded)
                return;

            _dirty = true;
            DirtyText.Visibility = Visibility.Visible;
            if (_serviceRunning)
                SaveButton.IsEnabled = true;
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
                ModeActionStatus.Text = ServiceControl.LastError ?? "未能启动安装程序。";
            }
        }

        private void OnRestartService(object sender, RoutedEventArgs e)
        {
            if (_installed)
            {
                ModeActionStatus.Text = ServiceControl.RestartInstalled()
                    ? "已请求重启服务（需要管理员权限）。"
                    : (ServiceControl.LastError ?? "重启被取消或失败。");
            }
            else
            {
                ModeActionStatus.Text = ServiceControl.RestartTemporary()
                    ? "已重启用户模式服务。"
                    : (ServiceControl.LastError ?? "未能重启用户模式服务。");
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
                ModeActionStatus.Text = ServiceControl.LastError ?? "未能卸载服务。";
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
            _suppressDirty = true;
            try
            {
                _current = settings;
                MonitoringBox.IsChecked = settings.MonitoringEnabled;
                StartupBox.IsChecked = settings.RunAtStartup;
                HideToTrayBox.IsChecked = settings.HideToTrayOnStartup;
                HideToTrayBox.IsEnabled = settings.RunAtStartup;
                NotificationsBox.IsChecked = settings.ShowNotifications;
                AdvancedModeBox.IsChecked = settings.AdvancedMode;
                ThemeBox.SelectedValue = settings.Theme;
                OsBox.SelectedValue = settings.OsOverride;
                RetentionBox.Text = settings.LogRetentionDays.ToString();
                UserScopeBox.SelectedValue = settings.TargetUserScope;
                CheckUpdatesBox.IsChecked = settings.CheckForUpdates;
                UpdateIntervalBox.SelectedValue = settings.UpdateCheckInterval;
            }
            finally
            {
                _suppressDirty = false;
            }

            _dirty = false;
            DirtyText.Visibility = Visibility.Collapsed;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            await SaveAsync();
        }

        private async Task<bool> SaveAsync()
        {
            int retention;
            if (!int.TryParse(RetentionBox.Text, out retention) || retention <= 0)
            {
                StatusText.Text = "日志保留天数必须是大于 0 的整数。";
                return false;
            }

            var settings = _current ?? new AppSettings();
            settings.MonitoringEnabled = MonitoringBox.IsChecked == true;
            settings.RunAtStartup = StartupBox.IsChecked == true;
            settings.HideToTrayOnStartup = HideToTrayBox.IsChecked == true;
            settings.ShowNotifications = NotificationsBox.IsChecked == true;
            settings.AdvancedMode = AdvancedModeBox.IsChecked == true;
            settings.Theme = (ThemeMode)ThemeBox.SelectedValue;
            settings.OsOverride = (OsFamily)OsBox.SelectedValue;
            settings.LogRetentionDays = retention;
            settings.TargetUserScope = (UserScopeMode)UserScopeBox.SelectedValue;
            settings.CheckForUpdates = CheckUpdatesBox.IsChecked == true;
            settings.UpdateCheckInterval = (UpdateInterval)UpdateIntervalBox.SelectedValue;

            var response = await _host.SendAsync(IpcProtocol.CommandSaveSettings, settings);
            if (!response.Success)
            {
                StatusText.Text = "服务未运行，设置未保存：" + response.Error;
                return false;
            }

            StatusText.Text = "已保存设置。";
            _dirty = false;
            DirtyText.Visibility = Visibility.Collapsed;
            _host.ApplyUserSettings(settings);
            _host.Refresh();
            return true;
        }

        private async void OnRunWizard(object sender, RoutedEventArgs e)
        {
            if (_dirty && !await SaveAsync())
                return;

            _host.ShowOnboarding();
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            if (_dirty && !_host.Confirm("放弃当前未保存的修改并重新载入？"))
                return;
            LoadAsync();
        }

        private async void OnCheckUpdates(object sender, RoutedEventArgs e)
        {
            CheckNowButton.IsEnabled = false;
            UpdateStatusText.Text = "正在检查...";

            try
            {
                var info = await UpdateChecker.CheckAsync();
                if (info != null && info.Available)
                {
                    UpdateStatusText.Text = "发现新版本 " + info.LatestTag;
                    if (_host.Confirm("发现新版本 " + info.LatestTag + "（当前 "
                                      + UpdateChecker.CurrentVersion() + "）。\n\n是否打开发布页面？")
                        && !string.IsNullOrEmpty(info.ReleaseUrl))
                    {
                        Process.Start(info.ReleaseUrl);
                    }
                }
                else
                {
                    UpdateStatusText.Text = "已是最新版本（" + UpdateChecker.CurrentVersion() + "）。";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "检查失败：" + ex.Message;
            }
            finally
            {
                CheckNowButton.IsEnabled = true;
            }
        }

        private void OnBackup(object sender, RoutedEventArgs e)
        {
            _host.ExportBackup();
        }

        private void OnRestore(object sender, RoutedEventArgs e)
        {
            _host.ImportBackup();
        }

        private void OnOpenDataFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "EverDefault");
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                StatusText.Text = "无法打开数据目录：" + ex.Message;
            }
        }

        private async void OnClearLog(object sender, RoutedEventArgs e)
        {
            if (!_host.Confirm("清空全部活动记录？\n这不会影响正在生效的保护规则。"))
                return;

            var response = await _host.SendAsync(IpcProtocol.CommandClearLog);
            StatusText.Text = response.Success
                ? "已清空活动记录。"
                : "清空失败：" + response.Error;

            if (response.Success)
                _host.Refresh();
        }
    }
}
