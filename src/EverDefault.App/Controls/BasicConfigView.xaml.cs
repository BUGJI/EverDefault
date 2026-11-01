using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EverDefault.Core.Model;
using EverDefault.Ipc;
using EverDefault.Registry.Associations;

namespace EverDefault.App
{
    public sealed class ExtensionOption : INotifyPropertyChanged
    {
        private bool _selected;

        public ExtensionOption(string extension, string category)
        {
            Extension = extension;
            Category = category;
        }

        public string Extension { get; private set; }

        public string Category { get; private set; }

        public string Label
        {
            get
            {
                return string.IsNullOrEmpty(Category)
                    ? Extension
                    : Extension + "  ·  " + Category;
            }
        }

        public bool IsSelected
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                OnPropertyChanged("IsSelected");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class NameSpaceOption : INotifyPropertyChanged
    {
        private bool _selected;

        public NameSpaceOption(NameSpaceScanEntry entry)
        {
            Entry = entry;
        }

        public NameSpaceScanEntry Entry { get; private set; }

        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Entry.DisplayName))
                    return Entry.DisplayName;
                return Entry.Guid;
            }
        }

        public string Detail
        {
            get
            {
                var target = string.IsNullOrWhiteSpace(Entry.TargetPath)
                    ? Entry.NameSpaceKeyPath
                    : Entry.TargetPath;
                return (Entry.Location ?? "此电脑") + " · " + target;
            }
        }

        public bool IsSelected
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                OnPropertyChanged("IsSelected");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class SettingPresetOption : INotifyPropertyChanged
    {
        private bool _selected;

        public SettingPresetOption(
            string title, string detail, string keyPath, string valueName,
            string expectedKind, string expectedData)
        {
            Title = title;
            Detail = detail;
            KeyPath = keyPath;
            ValueName = valueName;
            ExpectedKind = expectedKind;
            ExpectedData = expectedData;
        }

        public string Title { get; private set; }

        public string Detail { get; private set; }

        public string KeyPath { get; private set; }

        public string ValueName { get; private set; }

        public string ExpectedKind { get; private set; }

        public string ExpectedData { get; private set; }

        public bool IsSelected
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                OnPropertyChanged("IsSelected");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// The simplified "basic configuration" tab: searchable checklists, current defaults,
    /// and a visual preview. It never exposes raw registry fields.
    /// </summary>
    public partial class BasicConfigView : UserControl
    {
        private const string ExplorerAdvanced =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        private const string ExplorerPolicy =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        private IAppHost _host;
        private readonly ObservableCollection<AppInfo> _apps = new ObservableCollection<AppInfo>();
        private readonly DispatcherTimer _previewTimer;
        private readonly List<ExtensionOption> _extensionOptions = new List<ExtensionOption>();
        private readonly List<SettingPresetOption> _presetOptions = new List<SettingPresetOption>();
        private List<NameSpaceOption> _nsOptions = new List<NameSpaceOption>();

        private RuleModule _module = RuleModule.DefaultApp;
        private Dictionary<string, string> _appProgIdMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private AppInfo _scannedApp;
        private bool _initialized;

        public BasicConfigView()
            : this(null)
        {
        }

        public BasicConfigView(IAppHost host)
        {
            _host = host;
            InitializeComponent();

            BuildCategories();
            BuildExtensions();
            BuildPresets();
            RefreshExtensionList();

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _previewTimer.Tick += (sender, args) =>
            {
                _previewTimer.Stop();
                UpdatePreviewsNow();
            };

            AppBox.ItemsSource = _apps;
            _ = LoadApplicationsAsync();

            _initialized = true;
            UpdateModeVisibility();
            UpdatePreviewsNow();
        }

        private async System.Threading.Tasks.Task LoadApplicationsAsync()
        {
            try
            {
                var apps = await AppScanner.LoadApplicationsAsync();
                if (apps == null || apps.Count == 0)
                    return;

                var text = AppBox.Text;
                var selected = AppBox.SelectedItem as AppInfo;

                _apps.Clear();
                foreach (var app in apps)
                    _apps.Add(app);

                if (!string.IsNullOrWhiteSpace(text))
                    AppBox.Text = text;

                if (selected != null)
                {
                    AppBox.SelectedItem = _apps.FirstOrDefault(
                        a => string.Equals(a.ExePath, selected.ExePath, StringComparison.OrdinalIgnoreCase));
                }
                else if (_scannedApp != null)
                {
                    AppBox.SelectedItem = _apps.FirstOrDefault(
                        a => string.Equals(a.DisplayName, _scannedApp.DisplayName,
                            StringComparison.CurrentCultureIgnoreCase));
                }
            }
            catch (Exception)
            {
                // The list is optional; the user can still browse for an executable.
            }
        }

        public event EventHandler AdvancedRequested;

        public event EventHandler SelectionChanged;

        public void SetHost(IAppHost host)
        {
            _host = host;
        }

        public void Configure(RuleModule module)
        {
            _module = module;
            AppSection.Visibility = module == RuleModule.DefaultApp ? Visibility.Visible : Visibility.Collapsed;
            NameSpaceSection.Visibility = module == RuleModule.NameSpace ? Visibility.Visible : Visibility.Collapsed;
            CustomSection.Visibility = module == RuleModule.CustomRegistry ? Visibility.Visible : Visibility.Collapsed;
            ExistingHint.Visibility = Visibility.Collapsed;
            UpdatePreviews();
        }

        public void LoadRule(RuleBase rule)
        {
            if (!_initialized)
                return;

            ExistingHint.Visibility = Visibility.Collapsed;

            var app = rule as DefaultAppRule;
            if (app != null)
            {
                var hasProtocols = app.Protocols != null && app.Protocols.Count > 0;
                CurrentDefaultRadio.IsChecked = hasProtocols;
                SpecificAppRadio.IsChecked = !hasProtocols;

                _appProgIdMap = app.ProgIdMap != null
                    ? new Dictionary<string, string>(app.ProgIdMap, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var raw in app.Extensions ?? new List<string>())
                {
                    var ext = DefaultAppRule.NormalizeExtension(raw);
                    if (ext == null)
                        continue;

                    var progId = app.ResolveProgId(ext);
                    if (!string.IsNullOrEmpty(progId))
                        _appProgIdMap[ext] = progId;

                    SelectExtension(ext, true);
                }

                SelectProtocol("http", (app.Protocols ?? new List<string>()).Any(
                    p => string.Equals(DefaultAppRule.NormalizeProtocol(p), "http", StringComparison.OrdinalIgnoreCase)));
                SelectProtocol("https", (app.Protocols ?? new List<string>()).Any(
                    p => string.Equals(DefaultAppRule.NormalizeProtocol(p), "https", StringComparison.OrdinalIgnoreCase)));
                SelectProtocol("mailto", (app.Protocols ?? new List<string>()).Any(
                    p => string.Equals(DefaultAppRule.NormalizeProtocol(p), "mailto", StringComparison.OrdinalIgnoreCase)));

                _scannedApp = new AppInfo
                {
                    DisplayName = string.IsNullOrWhiteSpace(app.AppName)
                        ? (app.ProgId ?? app.ProtocolProgId ?? "当前程序")
                        : app.AppName,
                    ExePath = app.AppPath
                };

                if (_scannedApp != null)
                {
                    AppBox.Text = _scannedApp.DisplayName;
                    var match = _apps.FirstOrDefault(a => string.Equals(
                        a.DisplayName, _scannedApp.DisplayName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        AppBox.SelectedItem = match;
                }

                AppStatusText.Text = "已载入现有保护。修改后保存即可。";
                RefreshExtensionList();
                UpdateModeVisibility();
                UpdatePreviews();
                return;
            }

            ExistingHint.Visibility = Visibility.Visible;
            var nameSpace = rule as NameSpaceRule;
            if (nameSpace != null)
                NsPreview.ShowMessage("\uE8B7", "此电脑保护项",
                    string.IsNullOrWhiteSpace(rule.Name) ? "已在高级配置中设置。" : rule.Name,
                    "基础配置只负责新建保护，修改请用高级配置。");
            else
                CustomPreview.ShowSettings("系统设置保护项",
                    new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("名称", rule != null ? rule.Name : string.Empty)
                    },
                    "基础配置只负责新建保护，修改请用高级配置。");
        }

        public List<RuleBase> BuildRules(out string error)
        {
            error = null;

            if (ExistingHint.Visibility == Visibility.Visible)
            {
                error = "这条保护项包含高级设置，请切换到“高级配置”修改。";
                return null;
            }

            try
            {
                switch (_module)
                {
                    case RuleModule.DefaultApp:
                        return BuildDefaultApp(out error);
                    case RuleModule.NameSpace:
                        return BuildNameSpaces(out error);
                    default:
                        return BuildSettings(out error);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private List<RuleBase> BuildDefaultApp(out string error)
        {
            error = null;
            var extensions = SelectedExtensions();
            var protocols = SelectedProtocols();

            if (extensions.Count == 0 && protocols.Count == 0)
            {
                error = "请至少勾选一个文件类型或链接类型。";
                return null;
            }

            if (CurrentDefaultRadio.IsChecked == true)
            {
                var current = ProtectionBuilder.BuildCurrentRules(extensions, protocols);
                if (current.Count == 0)
                {
                    error = "所选项目没有可读取的当前默认程序，请换一个项目，或切换到“高级配置”手动指定。";
                    return null;
                }

                foreach (var rule in current)
                    ApplyDefaults(rule);

                return current.Cast<RuleBase>().ToList();
            }

            var app = SelectedApp();
            if (app == null)
            {
                error = "请选择要指定的程序，或点“扫描支持的格式”。";
                return null;
            }

            if (protocols.Count > 0)
            {
                error = "“指定一个程序”模式暂不支持链接类型；请改选“保护当前默认程序”，或到高级配置填写。";
                return null;
            }

            var supported = extensions.Where(e => _appProgIdMap.ContainsKey(e)).ToList();
            if (supported.Count == 0)
            {
                error = "这个程序没有为所选文件类型注册打开方式，请先点“扫描支持的格式”。";
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in supported)
                map[ext] = _appProgIdMap[ext];

            var specificRule = new DefaultAppRule
            {
                Name = RuleNaming.ForDefaultApp(supported, null, app.DisplayName),
                AppName = app.DisplayName,
                AppPath = app.ExePath,
                Extensions = supported,
                ProgIdMap = map,
                ManageOpenWith = true,
                ManageFileAssociation = true
            };

            ApplyDefaults(specificRule);
            return new List<RuleBase> { specificRule };
        }

        private List<RuleBase> BuildNameSpaces(out string error)
        {
            error = null;
            var selected = _nsOptions.Where(o => o.IsSelected).ToList();
            if (selected.Count == 0)
            {
                error = "请先点“扫描此电脑”，再勾选要清理的项目。";
                return null;
            }

            var rules = new List<RuleBase>();
            foreach (var option in selected)
            {
                var entry = option.Entry;
                var slash = (entry.NameSpaceKeyPath ?? string.Empty).LastIndexOf('\\');
                var root = slash > 0
                    ? entry.NameSpaceKeyPath.Substring(0, slash)
                    : entry.NameSpaceKeyPath;

                rules.Add(new NameSpaceRule
                {
                    Name = "清理「" + (string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Guid : entry.DisplayName) + "」",
                    Mode = RuleMode.Monitor,
                    Action = RuleAction.Delete,
                    Enabled = true,
                    PathPatterns = new List<string> { root },
                    MatchType = NameSpaceMatchType.Guid,
                    MatchPattern = entry.Guid,
                    DeleteScope = NameSpaceDeleteScope.NameSpaceOnly,
                    RefreshShell = true
                });
            }

            return rules;
        }

        private List<RuleBase> BuildSettings(out string error)
        {
            error = null;
            var selected = _presetOptions.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                error = "请至少勾选一项要保护的设置。";
                return null;
            }

            var rules = new List<RuleBase>();
            foreach (var preset in selected)
            {
                rules.Add(new CustomRegistryRule
                {
                    Name = preset.Title,
                    Mode = RuleMode.Monitor,
                    Action = RuleAction.Restore,
                    Enabled = true,
                    KeyPatterns = new List<string> { preset.KeyPath },
                    ValueName = preset.ValueName,
                    MatchType = ValueMatchType.Exact,
                    ExpectedKind = preset.ExpectedKind,
                    ExpectedData = preset.ExpectedData,
                    OnMismatch = RuleAction.Restore
                });
            }

            return rules;
        }

        private static void ApplyDefaults(RuleBase rule)
        {
            rule.Mode = RuleMode.Monitor;
            rule.Action = RuleAction.Restore;
            rule.Enabled = true;
        }

        private void BuildCategories()
        {
            foreach (var group in FormatGroups.All)
            {
                var button = new Button
                {
                    Content = group.Name,
                    Tag = group,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(10, 5, 10, 5),
                    ToolTip = group.Summary
                };
                button.Click += OnCategoryClick;
                CategoryPanel.Children.Add(button);
            }
        }

        private void BuildExtensions()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in FormatGroups.All)
            {
                foreach (var raw in group.Extensions)
                {
                    var ext = DefaultAppRule.NormalizeExtension(raw);
                    if (ext == null || !seen.Add(ext))
                        continue;

                    _extensionOptions.Add(new ExtensionOption(ext, group.Name));
                }
            }

            _extensionOptions.Sort((a, b) => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase));
        }

        private void BuildPresets()
        {
            _presetOptions.Add(new SettingPresetOption(
                "显示文件扩展名",
                "让资源管理器始终显示 .pdf、.exe 等扩展名，避免改错文件。",
                ExplorerAdvanced, "HideFileExt", "DWord", "0"));

            _presetOptions.Add(new SettingPresetOption(
                "显示隐藏文件",
                "资源管理器始终显示隐藏文件。",
                ExplorerAdvanced, "Hidden", "DWord", "1"));

            _presetOptions.Add(new SettingPresetOption(
                "关闭最近使用记录",
                "不在“快速访问”里记录最近打开过的文件。",
                ExplorerAdvanced, "Start_TrackDocs", "DWord", "0"));

            _presetOptions.Add(new SettingPresetOption(
                "禁止自动播放",
                "插入 U 盘或光盘时不自动运行。",
                ExplorerPolicy, "NoDriveTypeAutoRun", "DWord", "255"));

            PresetList.ItemsSource = _presetOptions;
        }

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
                return;

            UpdateModeVisibility();
            UpdatePreviews();
        }

        private void UpdateModeVisibility()
        {
            SpecificPanel.Visibility = SpecificAppRadio.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProtocolPanel.Visibility = CurrentDefaultRadio.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var app = AppScanner.Browse(Window.GetWindow(this), _apps);
            if (app == null)
                return;

            AppBox.Items.Refresh();
            AppBox.SelectedItem = app;
            AppBox.Text = app.DisplayName;
            _scannedApp = null;
            _appProgIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AppStatusText.Text = "已选择：" + app.ExePath + "，点“扫描支持的格式”。";
            UpdatePreviews();
        }

        private async void OnScanApp(object sender, RoutedEventArgs e)
        {
            var app = SelectedApp();
            if (app == null)
            {
                AppStatusText.Text = "请先选择一个应用。";
                return;
            }

            ScanAppButton.IsEnabled = false;
            AppStatusText.Text = "正在扫描 " + (app.ExeName ?? app.DisplayName) + " ...";

            try
            {
                var groups = await AppScanner.ScanAsync(app);
                _appProgIdMap = AppFormats.FlattenByExtension(groups);
                _scannedApp = app;
                AppStatusText.Text = _appProgIdMap.Count == 0
                    ? "没有找到这个应用注册的文件类型。"
                    : "已找到 " + _appProgIdMap.Count + " 种文件类型。勾选后保存。";
            }
            catch (Exception ex)
            {
                AppStatusText.Text = "扫描失败：" + ex.Message;
            }
            finally
            {
                ScanAppButton.IsEnabled = true;
            }

            RefreshExtensionList();
            UpdatePreviews();
        }

        private AppInfo SelectedApp()
        {
            var app = AppBox.SelectedItem as AppInfo;
            if (app != null)
                return app;

            var text = (AppBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                return _scannedApp;

            return _apps.FirstOrDefault(a => string.Equals(
                       a.DisplayName, text, StringComparison.CurrentCultureIgnoreCase))
                   ?? _scannedApp;
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized)
                return;
            RefreshExtensionList();
        }

        private void RefreshExtensionList()
        {
            var query = (SearchBox.Text ?? string.Empty).Trim();
            var filtered = _extensionOptions
                .Where(o => query.Length == 0
                            || o.Extension.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                            || o.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            ExtensionList.ItemsSource = filtered;
        }

        private void OnCategoryClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var group = button == null ? null : button.Tag as FormatGroupPreset;
            if (group == null)
                return;

            var options = _extensionOptions.Where(o => string.Equals(
                o.Category, group.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            var select = options.Any(o => !o.IsSelected);
            foreach (var option in options)
                option.IsSelected = select;

            RefreshExtensionList();
            UpdatePreviews();
        }

        private void OnExtensionClicked(object sender, RoutedEventArgs e)
        {
            UpdatePreviews();
        }

        private void OnProtocolClicked(object sender, RoutedEventArgs e)
        {
            UpdatePreviews();
        }

        private void OnPresetClicked(object sender, RoutedEventArgs e)
        {
            UpdatePreviews();
        }

        private void OnNsClicked(object sender, RoutedEventArgs e)
        {
            UpdatePreviews();
        }

        private async void OnScanNameSpace(object sender, RoutedEventArgs e)
        {
            if (_host == null || !_host.ServiceRunning)
            {
                NsStatusText.Text = "后台服务未运行，无法扫描。";
                return;
            }

            ScanNameSpaceButton.IsEnabled = false;
            NsStatusText.Text = "正在扫描此电脑...";

            try
            {
                var response = await _host.SendAsync(IpcProtocol.CommandScanNamespaces);
                if (!response.Success)
                {
                    NsStatusText.Text = "扫描失败：" + response.Error;
                    return;
                }

                var result = PipeJson.Deserialize<NameSpaceScanResult>(response.Payload);
                var entries = result == null || result.Entries == null
                    ? new List<NameSpaceScanEntry>()
                    : result.Entries;

                _nsOptions = entries.Select(entry => new NameSpaceOption(entry)).ToList();
                RefreshNameSpaceList();

                NsStatusText.Text = _nsOptions.Count == 0
                    ? "没有发现多余的项目。"
                    : "发现 " + _nsOptions.Count + " 个项目，勾选后保存即可移除。";
            }
            catch (Exception ex)
            {
                NsStatusText.Text = "扫描失败：" + ex.Message;
            }
            finally
            {
                ScanNameSpaceButton.IsEnabled = true;
            }

            UpdatePreviews();
        }

        private void OnNsSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized)
                return;
            RefreshNameSpaceList();
        }

        private void RefreshNameSpaceList()
        {
            var query = (NsSearchBox.Text ?? string.Empty).Trim();
            NsList.ItemsSource = _nsOptions
                .Where(o => query.Length == 0
                            || o.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                            || o.Detail.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private void OnNsSelectNone(object sender, RoutedEventArgs e)
        {
            foreach (var option in _nsOptions)
                option.IsSelected = false;
            RefreshNameSpaceList();
            UpdatePreviews();
        }

        private void OnGoAdvanced(object sender, RoutedEventArgs e)
        {
            var handler = AdvancedRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void UpdatePreviews()
        {
            if (!_initialized)
                return;

            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void UpdatePreviewsNow()
        {
            if (!_initialized)
                return;

            if (_module == RuleModule.DefaultApp)
            {
                var extensions = SelectedExtensions();
                var protocols = SelectedProtocols();
                var targets = new List<string>();
                targets.AddRange(extensions);
                if (protocols.Count > 0)
                    targets.Add(protocols.Count == 1 ? "链接类型" : protocols.Count + " 个链接类型");

                if (CurrentDefaultRadio.IsChecked == true)
                {
                    var names = new List<string>();
                    string path = null;
                    foreach (var ext in extensions)
                    {
                        var entry = AssociationCatalog.Resolve(ext, AssociationKind.FileExtension);
                        AddAppName(names, entry);
                        if (path == null && entry != null)
                            path = entry.AppPath;
                    }

                    foreach (var protocol in protocols)
                    {
                        var entry = AssociationCatalog.Resolve(protocol, AssociationKind.UrlProtocol);
                        AddAppName(names, entry);
                        if (path == null && entry != null)
                            path = entry.AppPath;
                    }

                    var appName = names.Count == 0
                        ? "当前默认程序"
                        : names.Count == 1 ? names[0] : string.Join(" / ", names);

                    Preview.ShowDefaultApp(appName, path, targets,
                        "启用后不会改变现在的默认设置；只有被其他程序改动时才改回。");
                }
                else
                {
                    var app = SelectedApp();
                    var supported = extensions.Where(e => _appProgIdMap.ContainsKey(e)).ToList();
                    Preview.ShowDefaultApp(
                        app == null ? "请选择程序" : app.DisplayName,
                        app == null ? null : app.ExePath,
                        supported,
                        app == null
                            ? "先选择程序并点“扫描支持的格式”。"
                            : "该程序支持 " + supported.Count + " 个已勾选的文件类型。");
                }
            }
            else if (_module == RuleModule.NameSpace)
            {
                var count = _nsOptions.Count(o => o.IsSelected);
                NsPreview.ShowMessage("\uE8B7", "整理此电脑",
                    count == 0 ? "还没有勾选要清理的项目。" : "将移除选中的 " + count + " 个项目。",
                    "如果程序重新创建这些图标，EverDefault 会再次移除。");
            }
            else
            {
                var selected = _presetOptions.Where(p => p.IsSelected).ToList();
                CustomPreview.ShowSettings(
                    selected.Count == 0 ? "系统设置保护" : "将保护 " + selected.Count + " 项设置",
                    selected.Select(p => new KeyValuePair<string, string>(p.Title, p.ExpectedData)),
                    "检测到修改时，会改回你保存时的值。");
            }

            var handler = SelectionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static void AddAppName(List<string> names, AssociationEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.AppName))
                return;
            if (!names.Contains(entry.AppName, StringComparer.OrdinalIgnoreCase))
                names.Add(entry.AppName);
        }

        private List<string> SelectedExtensions()
        {
            return _extensionOptions
                .Where(o => o.IsSelected)
                .Select(o => o.Extension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> SelectedProtocols()
        {
            var list = new List<string>();
            if (HttpCheck.IsChecked == true)
            {
                list.Add("http");
                list.Add("https");
            }
            if (MailtoCheck.IsChecked == true)
                list.Add("mailto");
            return list;
        }

        private void SelectExtension(string extension, bool selected)
        {
            var option = _extensionOptions.FirstOrDefault(o => string.Equals(
                o.Extension, extension, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                option = new ExtensionOption(extension, "其他");
                _extensionOptions.Add(option);
            }

            option.IsSelected = selected;
        }

        private void SelectProtocol(string protocol, bool selected)
        {
            if (string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase))
            {
                if (selected)
                    HttpCheck.IsChecked = true;
                return;
            }

            if (string.Equals(protocol, "mailto", StringComparison.OrdinalIgnoreCase))
                MailtoCheck.IsChecked = selected;
        }
    }
}
