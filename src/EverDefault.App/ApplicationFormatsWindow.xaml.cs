using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public partial class ApplicationFormatsWindow : Window
    {
        private readonly List<AppInfo> _apps = new List<AppInfo>();
        private AppInfo _scannedApp;

        public ApplicationFormatsWindow()
        {
            InitializeComponent();

            _apps.AddRange(AppScanner.LoadApplications());

            AppBox.ItemsSource = _apps;
            if (_apps.Count > 0)
                AppBox.SelectedIndex = 0;
        }

        /// <summary>Rules to create, filled when the user confirms.</summary>
        public List<DefaultAppRule> Rules { get; private set; }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var app = AppScanner.Browse(this, _apps);
            if (app == null)
                return;

            AppBox.Items.Refresh();
            AppBox.SelectedItem = app;
            StatusText.Text = "已选择：" + app.ExePath + "，点“扫描”。";
        }

        private async void OnScan(object sender, RoutedEventArgs e)
        {
            var app = AppBox.SelectedItem as AppInfo;
            if (app == null)
            {
                StatusText.Text = "请先选择一个应用，或点“浏览...”指定 exe。";
                return;
            }

            ScanButton.IsEnabled = false;
            ImportButton.IsEnabled = false;
            GroupList.ItemsSource = null;
            _scannedApp = app;
            StatusText.Text = "正在扫描 " + app.ExeName + " ...";

            List<FormatGroup> groups;
            try
            {
                groups = await AppScanner.ScanAsync(app);
            }
            catch (Exception ex)
            {
                StatusText.Text = "扫描失败：" + ex.Message;
                ScanButton.IsEnabled = true;
                return;
            }

            ScanButton.IsEnabled = true;
            GroupList.ItemsSource = groups;
            foreach (var group in groups)
                GroupList.SelectedItems.Add(group);

            if (groups.Count == 0)
            {
                StatusText.Text = "没有找到该应用注册的文件类型。可用“浏览...”换一个 exe 再试。";
                ImportButton.IsEnabled = false;
                return;
            }

            var extensionCount = groups.Sum(g => g.Extensions.Count);
            StatusText.Text = string.Format(
                "找到 {0} 个文件类型，按 ProgId 分为 {1} 组。核对后点“合并导入”（所有选中分组将合并为一条规则）。",
                extensionCount, groups.Count);
            ImportButton.IsEnabled = true;
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var item in GroupList.Items)
                GroupList.SelectedItems.Add(item);
        }

        private void OnSelectNone(object sender, RoutedEventArgs e)
        {
            GroupList.SelectedItems.Clear();
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var selected = GroupList.SelectedItems.Cast<FormatGroup>().ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "请至少选择一个分组。";
                return;
            }

            var extensions = new List<string>();
            var progIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in selected)
            {
                foreach (var raw in group.Extensions)
                {
                    var ext = DefaultAppRule.NormalizeExtension(raw);
                    if (ext == null)
                        continue;

                    if (!extensions.Contains(ext))
                        extensions.Add(ext);

                    progIdMap[ext] = group.ProgId;
                }
            }

            var appName = _scannedApp != null ? _scannedApp.DisplayName : null;
            Rules = new List<DefaultAppRule>
            {
                new DefaultAppRule
                {
                    Name = RuleNaming.ForDefaultApp(extensions, null, appName),
                    Mode = RuleMode.Monitor,
                    Action = RuleAction.Restore,
                    Enabled = true,
                    AppName = appName,
                    AppPath = _scannedApp != null ? _scannedApp.ExePath : null,
                    Extensions = extensions,
                    ProgIdMap = progIdMap,
                    ManageOpenWith = true,
                    ManageFileAssociation = true
                }
            };

            DialogResult = true;
        }
    }
}
