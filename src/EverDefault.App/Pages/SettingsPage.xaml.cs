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
            if (!_loaded)
                LoadAsync();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
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
