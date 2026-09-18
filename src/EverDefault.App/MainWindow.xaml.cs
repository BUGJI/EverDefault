using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EverDefault.Core.Engine;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    public partial class MainWindow : Window, IAppHost
    {
        private static readonly Brush OnlineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B)));
        private static readonly Brush OfflineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20)));
        private static readonly Brush NeutralBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

        private readonly PipeClient _client = new PipeClient();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private readonly Dictionary<string, UIElement> _pages = new Dictionary<string, UIElement>();
        private readonly List<IAppPage> _appPages = new List<IAppPage>();
        private readonly SettingsPage _settingsPage;

        private bool _refreshing;
        private bool _serviceRunning;
        private bool _settingsLoaded;
        private bool _updateBusy;

        public MainWindow()
        {
            InitializeComponent();

            var home = new HomePage(this);
            var defaultApp = new RuleModulePage(this, RuleModule.DefaultApp);
            var nameSpace = new RuleModulePage(this, RuleModule.NameSpace);
            var custom = new RuleModulePage(this, RuleModule.CustomRegistry);
            var log = new LogPage();
            _settingsPage = new SettingsPage(this);
            var about = new AboutPage();

            AddPage("home", home);
            AddPage("rule:DefaultApp", defaultApp);
            AddPage("rule:NameSpace", nameSpace);
            AddPage("rule:CustomRegistry", custom);
            AddPage("log", log);
            AddPage("settings", _settingsPage);
            AddPage("about", about);

            _timer.Tick += (sender, args) => Refresh();
            _timer.Start();

            NavHome.IsChecked = true;
        }

        public bool ServiceRunning
        {
            get { return _serviceRunning; }
        }

        private void AddPage(string key, UIElement page)
        {
            var appPage = page as IAppPage;
            if (appPage != null)
                _appPages.Add(appPage);

            page.Visibility = Visibility.Collapsed;
            _pages[key] = page;
            PageHost.Children.Add(page);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void OnNavChecked(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            if (button == null)
                return;

            var key = button.Tag as string;
            if (key == null || !_pages.ContainsKey(key))
                return;

            foreach (var pair in _pages)
                pair.Value.Visibility = pair.Key == key ? Visibility.Visible : Visibility.Collapsed;

            if (key == "settings")
                _settingsPage.OnActivated();
        }

        private async void OnMonitoringChanged(object sender, RoutedEventArgs e)
        {
            var enabled = MonitoringCheck.IsChecked == true;
            var response = await SendAsync(IpcProtocol.CommandSetMonitoring, enabled.ToString());
            if (!response.Success)
            {
                MonitoringCheck.IsChecked = !enabled;
                ShowInfo("服务未运行，无法切换监控状态：\n" + response.Error);
            }

            Refresh();
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        public async void Refresh()
        {
            if (_refreshing)
                return;

            _refreshing = true;
            try
            {
                var snapshot = await Task.Run(() => FetchSnapshot());
                ApplySnapshot(snapshot);
            }
            catch (Exception ex)
            {
                StatusText.Text = "错误：" + ex.Message;
                StatusText.Foreground = NeutralBrush;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private RefreshSnapshot FetchSnapshot()
        {
            var snapshot = new RefreshSnapshot();

            var statusResponse = _client.Send(IpcProtocol.CommandGetStatus);
            snapshot.ServiceRunning = statusResponse.Success;
            snapshot.StatusError = statusResponse.Error;
            if (!statusResponse.Success)
                return snapshot;

            snapshot.Status = PipeJson.Deserialize<ServiceStatus>(statusResponse.Payload);

            var rulesResponse = _client.Send(IpcProtocol.CommandGetRules);
            if (rulesResponse.Success)
            {
                var result = RuleCodec.DeserializeObject<RuleListResult>(rulesResponse.Payload);
                snapshot.Rules = result.Rules.Select(r => new RuleRow(r)).ToList();
                snapshot.Conflicts = result.Conflicts;
            }

            var logResponse = _client.Send(IpcProtocol.CommandGetLog, new LogQuery { Max = 200 });
            if (logResponse.Success)
            {
                var page = PipeJson.Deserialize<Ipc.LogPage>(logResponse.Payload);
                var ruleNames = snapshot.Rules != null
                    ? snapshot.Rules.GroupBy(r => r.Rule.Id).ToDictionary(g => g.Key, g => g.First().Name)
                    : new Dictionary<Guid, string>();
                snapshot.Logs = page.Entries.Select(x => new LogRow(x, ruleNames)).ToList();
            }

            return snapshot;
        }

        private void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;

            if (!snapshot.ServiceRunning)
            {
                StatusText.Text = "服务未运行：" + snapshot.StatusError;
                StatusText.Foreground = OfflineBrush;
                MonitoringCheck.IsEnabled = false;
            }
            else
            {
                MonitoringCheck.IsEnabled = true;
                StatusText.Foreground = OnlineBrush;
                var status = snapshot.Status;
                MonitoringCheck.IsChecked = status.MonitoringEnabled;
                StatusText.Text = string.Format("{0} | 版本 {1} | 监听 {2} | 规则 {3} | 启动 {4:HH:mm:ss}",
                    status.OsDescription, status.Version, status.ActiveWatchers, status.RuleCount,
                    status.StartedUtc.ToLocalTime());
            }

            foreach (var page in _appPages)
                page.ApplySnapshot(snapshot);

            if (snapshot.ServiceRunning && !_settingsLoaded)
            {
                _settingsLoaded = true;
                LoadAndApplySettings();
            }
        }

        private async void LoadAndApplySettings()
        {
            try
            {
                var response = await SendAsync(IpcProtocol.CommandGetSettings);
                if (!response.Success)
                    return;

                var settings = PipeJson.Deserialize<AppSettings>(response.Payload);
                if (settings != null)
                    ApplyUserSettings(settings);
            }
            catch (Exception)
            {
                // Settings are optional; ignore transient failures.
            }
        }

        public void ApplyUserSettings(AppSettings settings)
        {
            if (settings == null)
                return;

            try
            {
                ThemeManager.Apply(settings.Theme);
            }
            catch (Exception)
            {
                // A theme failure must not block the remaining settings.
            }

            try
            {
                StartupRegistration.Apply(settings.RunAtStartup, settings.HideToTrayOnStartup);
            }
            catch (Exception)
            {
                // Non-fatal.
            }

            if (settings.CheckForUpdates)
                CheckForUpdates(settings.UpdateCheckInterval);
        }

        private async void CheckForUpdates(UpdateInterval interval)
        {
            if (_updateBusy || !UpdateChecker.IsDue(interval))
                return;

            _updateBusy = true;
            try
            {
                var info = await UpdateChecker.CheckAsync();
                if (info != null && info.Available)
                {
                    var result = MessageBox.Show(this,
                        "发现新版本 " + info.LatestTag + "（当前 " + UpdateChecker.CurrentVersion() + "）。\n\n是否打开发布页面？",
                        "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(info.ReleaseUrl))
                        Process.Start(info.ReleaseUrl);
                }
            }
            catch (Exception)
            {
                // Network failures are non-fatal.
            }
            finally
            {
                _updateBusy = false;
            }
        }

        private async void OnExport(object sender, RoutedEventArgs e)
        {
            var response = await SendAsync(IpcProtocol.CommandGetRules);
            if (!response.Success)
            {
                ShowInfo("服务未运行，无法导出：" + response.Error);
                return;
            }

            var result = RuleCodec.DeserializeObject<RuleListResult>(response.Payload);
            if (result == null || result.Rules.Count == 0)
            {
                ShowInfo("当前没有规则可导出。");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                FileName = "everdefault-rules.json",
                DefaultExt = ".json"
            };

            bool? accepted;
            _timer.Stop();
            try
            {
                accepted = dialog.ShowDialog(this);
            }
            finally
            {
                _timer.Start();
            }

            if (accepted != true)
                return;

            try
            {
                var document = RuleDocumentMapper.ToDocument(result.Rules);
                File.WriteAllText(dialog.FileName, PipeJson.Serialize(document));
                ShowInfo("已导出 " + result.Rules.Count + " 条规则：\n" + dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowWarning("导出失败：" + ex.Message);
            }
        }

        private async void OnImport(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json"
            };

            bool? accepted;
            _timer.Stop();
            try
            {
                accepted = dialog.ShowDialog(this);
            }
            finally
            {
                _timer.Start();
            }

            if (accepted != true)
                return;

            RuleDocument document;
            try
            {
                document = PipeJson.Deserialize<RuleDocument>(File.ReadAllText(dialog.FileName));
            }
            catch (Exception ex)
            {
                ShowWarning("文件解析失败：" + ex.Message);
                return;
            }

            if (document == null || document.Rules == null || document.Rules.Count == 0)
            {
                ShowInfo("文件中没有可导入的规则。");
                return;
            }

            var succeeded = 0;
            var errors = new List<string>();

            foreach (var dto in document.Rules)
            {
                string error;
                var rule = RuleDocumentMapper.ToRule(dto, out error);
                if (rule == null)
                {
                    errors.Add(error);
                    continue;
                }

                var response = await SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
                if (response.Success)
                    succeeded++;
                else
                    errors.Add((dto.Name ?? "?") + ": " + response.Error);
            }

            Refresh();

            var message = "导入完成：成功 " + succeeded + " 条";
            if (errors.Count > 0)
                message += "，失败 " + errors.Count + " 条：\n" + string.Join("\n", errors.Take(8));

            if (errors.Count > 0)
                ShowWarning(message);
            else
                ShowInfo(message);
        }

        public Task<IpcResponse> SendAsync(string command, object payload = null)
        {
            return Task.Run(() => _client.Send(command, payload));
        }

        public void ShowInfo(string message)
        {
            MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public bool Confirm(string message)
        {
            return MessageBox.Show(this, message, "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
