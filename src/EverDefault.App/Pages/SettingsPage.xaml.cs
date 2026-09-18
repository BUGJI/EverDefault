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

            OsBox.ItemsSource = Display.Options<OsFamily>(Display.Os);
            UserScopeBox.ItemsSource = Display.Options<UserScopeMode>(Display.UserScope);

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

            MonitoringBox.IsChecked = settings.MonitoringEnabled;
            StartupBox.IsChecked = settings.RunAtStartup;
            OsBox.SelectedValue = settings.OsOverride;
            RetentionBox.Text = settings.LogRetentionDays.ToString();
            UserScopeBox.SelectedValue = settings.TargetUserScope;
            AntivirusBox.IsChecked = settings.RemindAntivirusWhitelist;

            _loaded = true;
            SaveButton.IsEnabled = true;
            StatusText.Text = "已载入当前设置。";
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
                OsOverride = (OsFamily)OsBox.SelectedValue,
                LogRetentionDays = retention,
                TargetUserScope = (UserScopeMode)UserScopeBox.SelectedValue,
                RemindAntivirusWhitelist = AntivirusBox.IsChecked == true
            };

            var response = await _host.SendAsync(IpcProtocol.CommandSaveSettings, settings);
            if (!response.Success)
            {
                StatusText.Text = "服务未运行，设置未保存：" + response.Error;
                return;
            }

            StatusText.Text = "已保存设置。";
            _host.Refresh();
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            LoadAsync();
        }
    }
}
