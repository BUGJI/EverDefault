using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EverDefault.Core.Engine;
using EverDefault.App.Tray;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    public partial class MainWindow : Window, IAppHost
    {
        private readonly PipeClient _client = new PipeClient();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private readonly Dictionary<string, UIElement> _pages = new Dictionary<string, UIElement>();
        private readonly SettingsPage _settingsPage;
        private readonly ActivityPage _activityPage;
        private readonly ProtectionPage _protectionPage;
        private readonly HomePage _homePage;
        private readonly NotificationCenter _notifications = new NotificationCenter();
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly Queue<ToastMessage> _toasts = new Queue<ToastMessage>();
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer();
        private ToastMessage _currentToast;

        private const int LogPageSize = 500;

        private bool _refreshing;
        private bool _serviceRunning;
        private bool _settingsLoaded;
        private bool _reloadRequested;
        private bool _updateBusy;
        private bool _onboardingShown;
        private string _currentPageKey = "home";
        private RefreshSnapshot _lastSnapshot;
        private AppSettings _lastSettings;

        private List<RuleRow> _cachedRules;
        private List<string> _cachedConflicts;
        private List<Guid> _cachedConflictingIds;
        private List<ChangeLogEntry> _cachedLogEntries;
        private List<ActivityItem> _cachedLogs;
        private readonly Dictionary<long, ActivityItem> _activityCache =
            new Dictionary<long, ActivityItem>();
        private int _cachedRulesRevision = -1;
        private long _cachedLogLastId = -1;
        private DateTime _cachedStartedUtc = DateTime.MinValue;
        private bool _snapshotLoaded;

        public MainWindow()
        {
            InitializeComponent();
            WindowSizing.Clamp(this);
            WindowPlacement.Restore(this);

            _homePage = new HomePage(this);
            _protectionPage = new ProtectionPage(this);
            _activityPage = new ActivityPage(this);
            var defaultApp = new RuleModulePage(this, RuleModule.DefaultApp);
            var nameSpace = new RuleModulePage(this, RuleModule.NameSpace);
            var custom = new RuleModulePage(this, RuleModule.CustomRegistry);
            _settingsPage = new SettingsPage(this);
            var about = new AboutPage();

            AddPage("home", _homePage);
            AddPage("protection", _protectionPage);
            AddPage("activity", _activityPage);
            AddPage("rule:DefaultApp", defaultApp);
            AddPage("rule:NameSpace", nameSpace);
            AddPage("rule:CustomRegistry", custom);
            AddPage("settings", _settingsPage);
            AddPage("about", about);

            _notifications.Notify += message =>
            {
                var handler = NotificationRequested;
                if (handler != null)
                    handler(message);
            };

            _timer.Tick += (sender, args) => Refresh();
            _timer.Start();

            _toastTimer.Interval = TimeSpan.FromSeconds(4);
            _toastTimer.Tick += (sender, args) => HideToast();
            _toastTimer.Stop();

            IsVisibleChanged += (sender, args) =>
            {
                if (IsVisible)
                    ApplyToCurrentPage(_lastSnapshot);
            };

            NavHome.IsChecked = true;
        }

        /// <summary>Raised when a balloon should be shown; the tray icon handles it.</summary>
        public event Action<NotificationMessage> NotificationRequested;

        /// <summary>Raised with a short status line and the current protection state.</summary>
        public event Action<string, TrayProtectionState> ProtectionStateChanged;

        public bool ServiceRunning
        {
            get { return _serviceRunning; }
        }

        private void AddPage(string key, UIElement page)
        {
            page.Visibility = Visibility.Collapsed;
            _pages[key] = page;
            PageHost.Children.Add(page);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            WindowPlacement.Save(this);
            base.OnClosing(e);
        }

        private void OnMinimize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeRestore(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (MaximizeButton == null)
                return;

            var maximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = IconFontSupport.IsAvailable
                ? (maximized ? "\uE923" : "\uE922")
                : (maximized ? "还原" : "最大化");
            MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
            System.Windows.Automation.AutomationProperties.SetName(
                MaximizeButton, maximized ? "还原" : "最大化");

            if (WindowState != WindowState.Minimized && IsVisible)
                ApplyToCurrentPage(_lastSnapshot);
        }



        public void Navigate(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
                return;

            RadioButton target = null;
            switch (pageKey)
            {
                case "home": target = NavHome; break;
                case "protection": target = NavProtection; break;
                case "activity": target = NavActivity; break;
                case "settings": target = NavSettings; break;
                case "about": target = NavAbout; break;
                case "rule:DefaultApp": target = NavDefaultApp; break;
                case "rule:NameSpace": target = NavNameSpace; break;
                case "rule:CustomRegistry": target = NavCustom; break;
            }

            if (target != null)
                target.IsChecked = true;
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

            _currentPageKey = key;

            var appPage = _pages[key] as IAppPage;
            if (appPage != null && _lastSnapshot != null)
                appPage.ApplySnapshot(_lastSnapshot);

            if (key == "settings")
                _settingsPage.OnActivated();
        }

        private IAppPage CurrentPage()
        {
            UIElement page;
            return _pages.TryGetValue(_currentPageKey, out page) ? page as IAppPage : null;
        }

        private void ApplyToCurrentPage(RefreshSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            var page = CurrentPage();
            if (page != null)
                page.ApplySnapshot(snapshot);
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
                SetStatus("连接后台保护时出错：" + ex.Message, "DangerBrush", true);
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

            var status = PipeJson.Deserialize<ServiceStatus>(statusResponse.Payload);
            snapshot.Status = status;
            if (status == null)
                return snapshot;

            var serviceRestarted = !_snapshotLoaded || status.StartedUtc != _cachedStartedUtc;

            // Older engines do not report revisions; fall back to fetching every poll.
            var revisioned = status.RulesRevision != 0 || status.LogLastId != 0;

            if (!revisioned || serviceRestarted || status.RulesRevision != _cachedRulesRevision)
            {
                var rulesResponse = _client.Send(IpcProtocol.CommandGetRules);
                if (rulesResponse.Success)
                {
                    var result = RuleCodec.DeserializeObject<RuleListResult>(rulesResponse.Payload);
                    if (result != null)
                    {
                        _cachedRules = result.Rules.Select(r => new RuleRow(r)).ToList();
                        _cachedConflicts = result.Conflicts ?? new List<string>();
                        _cachedConflictingIds = result.ConflictingRuleIds ?? new List<Guid>();
                        _cachedRulesRevision = status.RulesRevision;
                        _activityCache.Clear();
                        snapshot.RulesChanged = true;
                    }
                }
            }

            snapshot.Rules = _cachedRules;
            snapshot.Conflicts = _cachedConflicts;
            snapshot.ConflictingRuleIds = _cachedConflictingIds;

            var logReset = !revisioned || serviceRestarted || status.LogLastId < _cachedLogLastId;
            if (logReset)
            {
                FetchFullLog(snapshot);
            }
            else if (status.LogLastId > _cachedLogLastId)
            {
                if (!FetchIncrementalLog())
                    FetchFullLog(snapshot);
                else
                    snapshot.LogsChanged = true;
            }

            // Rule names/targets appear in log text; rebuild when the rules changed. A full
            // log reset already rebuilt everything with the new rules.
            if (snapshot.RulesChanged && !logReset && _cachedLogEntries != null)
            {
                _activityCache.Clear();
                var ruleMap = CurrentRuleMap();
                foreach (var entry in _cachedLogEntries)
                {
                    var item = ActivityFactory.BuildItem(entry, ruleMap);
                    if (item != null)
                        _activityCache[entry.Id] = item;
                }

                snapshot.LogsChanged = true;
            }

            if (snapshot.LogsChanged || _cachedLogs == null)
            {
                _cachedLogs = new List<ActivityItem>(_cachedLogEntries == null ? 0 : _cachedLogEntries.Count);
                if (_cachedLogEntries != null)
                {
                    foreach (var entry in _cachedLogEntries)
                    {
                        ActivityItem item;
                        if (_activityCache.TryGetValue(entry.Id, out item))
                            _cachedLogs.Add(item);
                    }
                }
            }

            snapshot.Logs = _cachedLogs;

            _cachedStartedUtc = status.StartedUtc;
            _snapshotLoaded = true;
            return snapshot;
        }

        private void FetchFullLog(RefreshSnapshot snapshot)
        {
            var response = _client.Send(IpcProtocol.CommandGetLog, new LogQuery { Max = LogPageSize });
            if (!response.Success)
                return;

            var page = PipeJson.Deserialize<LogPage>(response.Payload);
            var entries = page == null ? new List<ChangeLogEntry>() : page.Entries;
            _cachedLogEntries = new List<ChangeLogEntry>(entries);
            _cachedLogLastId = entries.Count > 0
                ? entries[0].Id
                : (page == null ? 0 : page.LastId);

            _activityCache.Clear();
            var ruleMap = CurrentRuleMap();
            foreach (var entry in _cachedLogEntries)
            {
                var item = ActivityFactory.BuildItem(entry, ruleMap);
                if (item != null)
                    _activityCache[entry.Id] = item;
            }

            snapshot.LogsChanged = true;
        }

        private bool FetchIncrementalLog()
        {
            var response = _client.Send(IpcProtocol.CommandGetLog, new LogQuery
            {
                AfterId = _cachedLogLastId,
                Max = LogPageSize
            });
            if (!response.Success)
                return false;

            var page = PipeJson.Deserialize<LogPage>(response.Payload);
            var entries = page == null ? new List<ChangeLogEntry>() : page.Entries;
            if (entries.Count == 0)
                return true;

            if (_cachedLogEntries == null || entries[0].Id > _cachedLogLastId + 1)
                return false;

            var ruleMap = CurrentRuleMap();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                _cachedLogEntries.Insert(0, entry);
                var item = ActivityFactory.BuildItem(entry, ruleMap);
                if (item != null)
                    _activityCache[entry.Id] = item;
            }

            while (_cachedLogEntries.Count > LogPageSize)
            {
                var removed = _cachedLogEntries[_cachedLogEntries.Count - 1];
                _cachedLogEntries.RemoveAt(_cachedLogEntries.Count - 1);
                _activityCache.Remove(removed.Id);
            }

            _cachedLogLastId = entries[entries.Count - 1].Id;
            return true;
        }

        private Dictionary<Guid, RuleBase> CurrentRuleMap()
        {
            if (_cachedRules == null)
                return new Dictionary<Guid, RuleBase>();

            return _cachedRules
                .GroupBy(r => r.Rule.Id)
                .ToDictionary(g => g.Key, g => g.First().Rule);
        }

        private void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;
            snapshot.ServiceStarting = !snapshot.ServiceRunning
                                       && (DateTime.UtcNow - _startedUtc).TotalSeconds < 10;
            _lastSnapshot = snapshot;

            ServiceBanner.Visibility = snapshot.ServiceRunning ? Visibility.Collapsed : Visibility.Visible;
            ServiceBannerText.Text = snapshot.ServiceStarting
                ? "正在启动后台保护，请稍候…"
                : "后台保护未运行，保护可能没有生效。以下显示的是上次读取到的数据。";
            ServiceBannerButton.Visibility = snapshot.ServiceStarting
                ? Visibility.Collapsed
                : Visibility.Visible;

            var protectionPaused = false;
            var trayState = TrayProtectionState.Unavailable;

            if (!snapshot.ServiceRunning)
            {
                SetStatus(snapshot.ServiceStarting ? "正在启动后台保护…" : "后台保护未运行",
                    snapshot.ServiceStarting ? "WarningBrush" : "DangerBrush", true);
            }
            else
            {
                var status = snapshot.Status;
                var now = DateTime.UtcNow;
                protectionPaused = status.PauseUntilUtc.HasValue && status.PauseUntilUtc.Value > now;
                var count = snapshot.Rules == null ? 0 : snapshot.Rules.Count;

                if (!status.MonitoringEnabled)
                {
                    SetStatus("保护已关闭", "WarningBrush", true);
                    trayState = TrayProtectionState.Disabled;
                }
                else if (protectionPaused)
                {
                    SetStatus("保护已暂停", "WarningBrush");
                    trayState = TrayProtectionState.Paused;
                }
                else
                {
                    SetStatus(
                        "保护中 · " + count + " 项 · 已拦截 " + status.BlockedCount + " 次",
                        "SuccessBrush");
                    trayState = TrayProtectionState.Active;
                }
            }

            var stateHandler = ProtectionStateChanged;
            if (stateHandler != null)
                stateHandler(StatusText.Text, trayState);

            if (IsVisible && WindowState != WindowState.Minimized)
                ApplyToCurrentPage(snapshot);

            if (snapshot.LogsChanged)
                _notifications.Process(snapshot.Logs, _lastSettings);

            if (snapshot.ServiceRunning && !_settingsLoaded)
            {
                _settingsLoaded = true;
                LoadAndApplySettings();
            }

            if (snapshot.ServiceRunning && snapshot.Status != null
                && snapshot.Status.HostMode == ServiceHostMode.Service && !_reloadRequested)
            {
                _reloadRequested = true;
                _ = SendAsync(IpcProtocol.CommandReloadRules);
            }

            if (_lastSettings != null)
                MaybeShowOnboarding(_lastSettings);
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

            _lastSettings = settings;

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

            ApplyAdvancedMode(settings.AdvancedMode);
            _homePage.ApplySettings(settings);

            if (settings.CheckForUpdates)
                CheckForUpdates(settings.UpdateCheckInterval);

            MaybeShowOnboarding(settings);
        }

        private void ApplyAdvancedMode(bool advanced)
        {
            var visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            AdvancedHeader.Visibility = visibility;
            NavDefaultApp.Visibility = visibility;
            NavNameSpace.Visibility = visibility;
            NavCustom.Visibility = visibility;
            ExportButton.Visibility = visibility;
            ImportButton.Visibility = visibility;

            if (!advanced && _currentPageKey.StartsWith("rule:", StringComparison.OrdinalIgnoreCase))
                Navigate("home");
        }

        private void MaybeShowOnboarding(AppSettings settings)
        {
            if (_onboardingShown)
                return;

            if (settings.OnboardingCompleted || OnboardingState.IsCompleted)
            {
                OnboardingState.MarkCompleted();
                return;
            }

            if (!_serviceRunning || !IsVisible)
                return;

            _onboardingShown = true;
            ShowWizard(settings);
        }

        public async void ShowOnboarding()
        {
            if (!_serviceRunning)
            {
                ShowInfo("请先启动后台保护，再运行首次设置向导。");
                return;
            }

            var settings = _lastSettings;
            if (settings == null)
            {
                try
                {
                    var response = await SendAsync(IpcProtocol.CommandGetSettings);
                    if (response.Success)
                        settings = PipeJson.Deserialize<AppSettings>(response.Payload);
                }
                catch (Exception)
                {
                    // Reported below; do not fall back to defaults or saving would overwrite settings.
                }
            }

            if (settings == null)
            {
                ShowWarning("暂时无法读取当前设置，请稍后再试。");
                return;
            }

            _onboardingShown = true;
            ShowWizard(settings);
        }

        private void ShowWizard(AppSettings settings)
        {
            var existing = _lastSnapshot == null || _lastSnapshot.Rules == null
                ? new List<RuleBase>()
                : _lastSnapshot.Rules.Select(r => r.Rule).ToList();

            var window = new FirstRunWindow(this, settings, existing) { Owner = this };
            window.ShowDialog();
            OnboardingState.MarkCompleted();

            if (!settings.OnboardingCompleted)
            {
                settings.OnboardingCompleted = true;
                _ = SaveSettingsSilently(settings);
            }

            Refresh();
        }

        private async Task SaveSettingsSilently(AppSettings settings)
        {
            try
            {
                await SendAsync(IpcProtocol.CommandSaveSettings, settings);
                ApplyUserSettings(settings);
            }
            catch (Exception)
            {
                // Onboarding state is best-effort; the wizard can be reopened from Settings.
            }
        }

        public bool ConfirmExit()
        {
            return _settingsPage == null
                   || !_settingsPage.HasUnsavedChanges
                   || Confirm("设置页还有未保存的修改，退出后会丢失。\n\n仍要退出吗？");
        }

        public void OpenRuleById(Guid? ruleId)
        {
            Navigate("activity");
            _activityPage.Highlight(ruleId);

            if (_lastSnapshot == null || _lastSnapshot.Rules == null || !ruleId.HasValue)
                return;

            var row = _lastSnapshot.Rules.FirstOrDefault(r => r.Rule.Id == ruleId.Value);
            if (row == null)
                return;

            var window = new RuleEditorWindow(this, row.Rule.Module, row.Rule) { Owner = this };
            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            SaveRules(window.Rules);
        }

        public async void TogglePause()
        {
            if (_lastSnapshot == null || _lastSnapshot.Status == null)
                return;

            var status = _lastSnapshot.Status;
            var paused = status.PauseUntilUtc.HasValue && status.PauseUntilUtc.Value > DateTime.UtcNow;

            IpcResponse response;
            if (paused)
                response = await SendAsync(IpcProtocol.CommandResumeAll);
            else if (!status.MonitoringEnabled)
                response = await SendAsync(IpcProtocol.CommandSetMonitoring, true.ToString());
            else
                response = await SendAsync(IpcProtocol.CommandPauseAll, 60);

            if (!response.Success)
                ShowWarning("操作失败：" + response.Error);

            Refresh();
        }

        public async void PauseFor(int minutes)
        {
            if (_lastSnapshot == null || _lastSnapshot.Status == null)
                return;

            if (minutes > 0 && !_lastSnapshot.Status.MonitoringEnabled)
                await SendAsync(IpcProtocol.CommandSetMonitoring, true.ToString());

            var response = minutes > 0
                ? await SendAsync(IpcProtocol.CommandPauseAll, minutes)
                : await SendAsync(IpcProtocol.CommandResumeAll);

            if (!response.Success)
                ShowWarning("操作失败：" + response.Error);

            Refresh();
        }

        private void SaveRules(IEnumerable<RuleBase> rules)
        {
            _ = SaveRulesAsync(rules);
        }

        private async Task SaveRulesAsync(IEnumerable<RuleBase> rules)
        {
            var list = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => r != null)
                .ToList();
            if (list.Count == 0)
                return;

            await RuleApply.SaveAndRunAsync(this, list);
            Refresh();
        }

        private async void CheckForUpdates(UpdateInterval interval)
        {
            if (_updateBusy || !UpdateChecker.IsDue(interval))
                return;

            _updateBusy = true;
            try
            {
                var info = await UpdateChecker.CheckAsync();
                if (info != null && info.Available && IsVisible)
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

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void OnStartServiceFromBanner(object sender, RoutedEventArgs e)
        {
            ServiceConnector.TryStart(this);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                Refresh();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_currentPageKey == "activity")
                {
                    _activityPage.FocusSearch();
                }
                else if (_currentPageKey.StartsWith("rule:", StringComparison.OrdinalIgnoreCase))
                {
                    var rulePage = _pages[_currentPageKey] as RuleModulePage;
                    if (rulePage != null)
                        rulePage.FocusSearch();
                }
                else
                {
                    if (_currentPageKey != "protection")
                        Navigate("protection");
                    _protectionPage.FocusSearch();
                }

                e.Handled = true;
            }
        }

        public void ShowActivity(string filter)
        {
            Navigate("activity");
            _activityPage.SetFilter(filter);
        }

        public void ExportBackup()
        {
            _ = ExportBackupAsync();
        }

        public void ImportBackup()
        {
            _ = ImportBackupAsync();
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            _ = ExportBackupAsync();
        }

        private async Task ExportBackupAsync()
        {
            var response = await SendAsync(IpcProtocol.CommandGetRules);
            if (!response.Success)
            {
                ShowInfo("后台保护未运行，无法导出：" + response.Error);
                return;
            }

            var result = RuleCodec.DeserializeObject<RuleListResult>(response.Payload);
            if (result == null || result.Rules.Count == 0)
            {
                ShowInfo("当前没有保护项可导出。");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                FileName = "everdefault-protections.json",
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
                ShowInfo("已导出 " + result.Rules.Count + " 条保护：\n" + dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowWarning("导出失败：" + ex.Message);
            }
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            _ = ImportBackupAsync();
        }

        private async Task ImportBackupAsync()
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
                ShowInfo("文件中没有可导入的保护。");
                return;
            }

            var succeeded = 0;
            var errors = new List<string>();
            var rules = new List<RuleBase>();

            foreach (var dto in document.Rules)
            {
                string error;
                var rule = RuleDocumentMapper.ToRule(dto, out error);
                if (rule == null)
                {
                    errors.Add(error);
                    continue;
                }

                rules.Add(rule);
            }

            SaveApplyOutcome outcome = null;
            if (rules.Count > 0)
            {
                outcome = await RuleApply.SaveAndRunAsync(
                    this, rules, showMessages: false);
                if (outcome.Saved)
                    succeeded = rules.Count;
                else
                    errors.Add("批量导入失败: " + outcome.Error);
            }

            Refresh();

            var message = "导入完成：成功 " + succeeded + " 条";
            if (outcome != null && outcome.Saved)
            {
                if (outcome.Declined)
                    message += "\n删除类保护已导入，但未立即应用。";
                else if (outcome.Result != null)
                    message += "\n" + outcome.Result.Message;
            }

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
            EnqueueToast(message, false);
        }

        public void ShowWarning(string message)
        {
            EnqueueToast(message, true);
        }

        private void EnqueueToast(string message, bool warning)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _toasts.Enqueue(new ToastMessage { Text = message, Warning = warning });
            if (ToastBanner.Visibility != Visibility.Visible)
                ShowNextToast();
        }

        private void ShowNextToast()
        {
            if (_toasts.Count == 0)
            {
                ToastBanner.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
                return;
            }

            var message = _toasts.Dequeue();
            _currentToast = message;
            ToastText.Text = message.Text;
            ToastBanner.Background = TryFindResource(
                message.Warning ? "WarningSubtleBrush" : "SurfaceAltBrush") as Brush
                ?? Brushes.Transparent;
            ToastBanner.BorderBrush = TryFindResource(
                message.Warning ? "WarningBrush" : "AccentBrush") as Brush
                ?? Brushes.Gray;
            ToastBanner.Visibility = Visibility.Visible;

            RestartToastTimer();
        }

        private void RestartToastTimer()
        {
            _toastTimer.Stop();
            if (_currentToast == null)
                return;

            _toastTimer.Interval = TimeSpan.FromSeconds(_currentToast.Warning ? 12 : 5);
            _toastTimer.Start();
        }

        private void OnToastMouseEnter(object sender, MouseEventArgs e)
        {
            _toastTimer.Stop();
        }

        private void OnToastMouseLeave(object sender, MouseEventArgs e)
        {
            if (ToastBanner.Visibility == Visibility.Visible)
                RestartToastTimer();
        }

        private void OnToastClose(object sender, RoutedEventArgs e)
        {
            HideToast();
        }

        private void OnToastCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ToastText.Text ?? string.Empty);
            }
            catch (Exception)
            {
                // The clipboard can be locked by another process; copying is optional.
            }
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            _currentToast = null;
            ToastBanner.Visibility = Visibility.Collapsed;

            if (_toasts.Count > 0)
                ShowNextToast();
        }

        private sealed class ToastMessage
        {
            public string Text { get; set; }

            public bool Warning { get; set; }
        }

        public bool Confirm(string message)
        {
            var dialog = new ConfirmDialog(message) { Owner = IsVisible ? this : null };
            return dialog.ShowDialog() == true;
        }

        private void SetStatus(string text, string toneKey, bool colorText = false)
        {
            var tone = TryFindResource(toneKey) as Brush ?? Brushes.Gray;
            StatusText.Text = text;
            StatusText.Foreground = colorText
                ? tone
                : (TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Gray);

            if (StatusDot != null)
                StatusDot.Fill = tone;
        }
    }
}
