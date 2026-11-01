using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EverDefault.Core.Model;
using EverDefault.Ipc;
using EverDefault.Registry.Associations;

namespace EverDefault.App
{
    public sealed class Recommendation : INotifyPropertyChanged
    {
        private bool _selected;

        public Recommendation(string title, IList<string> extensions, IList<string> protocols)
        {
            Title = title;
            Extensions = extensions ?? new List<string>();
            Protocols = protocols ?? new List<string>();
        }

        public string Title { get; private set; }

        public IList<string> Extensions { get; private set; }

        public IList<string> Protocols { get; private set; }

        public bool Available { get; set; }

        public string Detail { get; set; }

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

    /// <summary>Three-step first-run wizard: recommended protections, auto-start, done.</summary>
    public partial class FirstRunWindow : DialogWindow
    {
        private readonly IAppHost _host;
        private readonly AppSettings _settings;
        private readonly List<RuleBase> _existing;
        private readonly List<Recommendation> _recommendations = new List<Recommendation>();

        public FirstRunWindow(IAppHost host, AppSettings settings, IEnumerable<RuleBase> existing = null)
        {
            _host = host;
            _settings = settings ?? new AppSettings();
            _existing = (existing ?? Enumerable.Empty<RuleBase>()).ToList();

            InitializeComponent();
            WindowSizing.Clamp(this);
            BuildRecommendations();

            StartupCheck.IsChecked = _settings.RunAtStartup;
            RefreshServiceText();
        }

        public bool Completed { get; private set; }

        private void RefreshServiceText()
        {
            var running = _host.ServiceRunning;
            ServiceText.Text = running
                ? "EverDefault 已连接后台保护，登录后会自动运行。"
                : "后台保护还没有运行，请先点下面的按钮启动。";
            StartServiceButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            StartButton.IsEnabled = running && _recommendations.Any(r => r.IsSelected && r.Available);
        }

        private void OnStartService(object sender, RoutedEventArgs e)
        {
            ServiceConnector.TryStart(_host);
            RefreshServiceText();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                RefreshServiceText();
            };
            timer.Start();
        }

        private void BuildRecommendations()
        {
            Add("默认浏览器（网页链接）", null, new[] { "http", "https" });
            Add("邮件客户端（邮件链接）", null, new[] { "mailto" });
            Add("PDF 阅读器", new[] { ".pdf" }, null);
            Add("图片查看器", new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" }, null);
            Add("视频播放器", new[] { ".mp4", ".mkv", ".avi", ".mov" }, null);
            Add("音乐播放器", new[] { ".mp3", ".flac", ".wav", ".m4a" }, null);
            Add("压缩包工具", new[] { ".zip", ".rar", ".7z" }, null);
            Add("文本编辑器", new[] { ".txt", ".md" }, null);

            RecommendationList.ItemsSource = _recommendations;
        }

        private void Add(string title, IList<string> extensions, IList<string> protocols)
        {
            var recommendation = new Recommendation(title, extensions, protocols);
            var app = FindCurrentApp(recommendation);
            var covered = IsFullyCovered(recommendation);

            recommendation.Available = app != null && !covered;
            recommendation.Detail = covered
                ? (app == null ? "已经在保护中。" : "当前：" + app + "。已经在保护中。")
                : app == null
                    ? "尚未设置默认程序，暂时跳过。"
                    : "当前：" + app;
            recommendation.IsSelected = app != null && !covered;

            _recommendations.Add(recommendation);
        }

        private bool IsFullyCovered(Recommendation recommendation)
        {
            var any = false;
            foreach (var extension in recommendation.Extensions)
            {
                var entry = AssociationCatalog.Resolve(extension, AssociationKind.FileExtension);
                if (entry == null)
                    continue;

                any = true;
                if (!ProtectionBuilder.IsCovered(_existing, entry))
                    return false;
            }

            foreach (var protocol in recommendation.Protocols)
            {
                var entry = AssociationCatalog.Resolve(protocol, AssociationKind.UrlProtocol);
                if (entry == null)
                    continue;

                any = true;
                if (!ProtectionBuilder.IsCovered(_existing, entry))
                    return false;
            }

            return any;
        }

        private bool IsCovered(string target, bool protocol)
        {
            if (protocol)
            {
                var entry = AssociationCatalog.Resolve(target, AssociationKind.UrlProtocol);
                return entry != null && ProtectionBuilder.IsCovered(_existing, entry);
            }

            var fileEntry = AssociationCatalog.Resolve(target, AssociationKind.FileExtension);
            return fileEntry != null && ProtectionBuilder.IsCovered(_existing, fileEntry);
        }

        private static string FindCurrentApp(Recommendation recommendation)
        {
            foreach (var extension in recommendation.Extensions)
            {
                var entry = AssociationCatalog.Resolve(extension, AssociationKind.FileExtension);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.AppName))
                    return entry.AppName;
            }

            foreach (var protocol in recommendation.Protocols)
            {
                var entry = AssociationCatalog.Resolve(protocol, AssociationKind.UrlProtocol);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.AppName))
                    return entry.AppName;
            }

            return null;
        }

        private async void OnStart(object sender, RoutedEventArgs e)
        {
            if (!_host.ServiceRunning)
            {
                SetStatus("后台服务未运行，暂时无法保存保护。请稍后再试，或到设置里启动服务。", true);
                return;
            }

            var selected = _recommendations.Where(r => r.IsSelected && r.Available).ToList();
            if (selected.Count == 0)
            {
                SetStatus("请至少勾选一项。", true);
                return;
            }

            StartButton.IsEnabled = false;
            SetStatus("正在保存保护...", false);

            var extensions = selected.SelectMany(r => r.Extensions)
                .Where(ext => !IsCovered(ext, false))
                .ToList();
            var protocols = selected.SelectMany(r => r.Protocols)
                .Where(p => !IsCovered(p, true))
                .ToList();

            _settings.RunAtStartup = StartupCheck.IsChecked == true;

            if (extensions.Count == 0 && protocols.Count == 0)
            {
                _settings.OnboardingCompleted = true;
                Completed = true;
                await _host.SendAsync(IpcProtocol.CommandSaveSettings, _settings);
                _host.ApplyUserSettings(_settings);
                DialogResult = true;
                return;
            }

            var rules = ProtectionBuilder.BuildCurrentRules(extensions, protocols);

            if (rules.Count == 0)
            {
                StartButton.IsEnabled = true;
                SetStatus("没有读取到当前默认程序，请稍后再试。", true);
                return;
            }

            var payload = rules.Select(RuleCodec.Serialize).ToList();
            var saveResponse = await _host.SendAsync(IpcProtocol.CommandSaveRulesAndRun, payload);

            _settings.OnboardingCompleted = true;
            Completed = true;
            await _host.SendAsync(IpcProtocol.CommandSaveSettings, _settings);
            _host.ApplyUserSettings(_settings);

            if (!saveResponse.Success)
            {
                SetStatus("保存失败：" + saveResponse.Error, true);
                StartButton.IsEnabled = true;
                return;
            }

            _host.Refresh();
            DialogResult = true;
        }

        private void SetStatus(string text, bool error)
        {
            StatusText.Text = text;
            StatusText.Foreground = TryFindResource(error ? "DangerBrush" : "TextSecondaryBrush") as Brush
                                    ?? Brushes.Gray;
        }

        private async void OnSkip(object sender, RoutedEventArgs e)
        {
            _settings.RunAtStartup = StartupCheck.IsChecked == true;
            _settings.OnboardingCompleted = true;
            Completed = true;
            if (_host.ServiceRunning)
                await _host.SendAsync(IpcProtocol.CommandSaveSettings, _settings);

            _host.ApplyUserSettings(_settings);
            DialogResult = false;
        }
    }
}
