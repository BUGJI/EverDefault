using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public partial class ApplicationFormatsWindow : Window
    {
        private readonly List<AppInfo> _apps = new List<AppInfo>();

        public ApplicationFormatsWindow()
        {
            InitializeComponent();

            foreach (var app in AppFormats.ListApplications())
                _apps.Add(app);

            AppBox.ItemsSource = _apps;
            if (_apps.Count > 0)
                AppBox.SelectedIndex = 0;
        }

        /// <summary>Rules to create, filled when the user confirms.</summary>
        public List<DefaultAppRule> Rules { get; private set; }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择应用程序",
                Filter = "应用程序 (*.exe)|*.exe|所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var existing = _apps.FirstOrDefault(
                a => string.Equals(a.ExePath, dialog.FileName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var fileName = Path.GetFileName(dialog.FileName);
                existing = new AppInfo
                {
                    DisplayName = fileName + "  (手动选择)",
                    ExeName = fileName,
                    ExePath = dialog.FileName
                };
                _apps.Add(existing);
                AppBox.Items.Refresh();
            }

            AppBox.SelectedItem = existing;
            StatusText.Text = "已选择：" + dialog.FileName + "，点“扫描”。";
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
            StatusText.Text = "正在扫描 " + app.ExeName + " ...";

            List<FormatGroup> groups;
            try
            {
                var exeName = app.ExeName;
                var exePath = app.ExePath;
                groups = await Task.Run(() => AppFormats.ScanFormats(exeName, exePath));
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
                "找到 {0} 个文件类型，按 ProgId 分为 {1} 组。核对后点“导入选中”。",
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

            Rules = new List<DefaultAppRule>();
            foreach (var group in selected)
            {
                Rules.Add(new DefaultAppRule
                {
                    Name = RuleNaming.ForDefaultApp(group.Extensions, group.ProgId),
                    Mode = RuleMode.Monitor,
                    Action = RuleAction.Restore,
                    Enabled = true,
                    Extensions = new List<string>(group.Extensions),
                    ProgId = group.ProgId,
                    ManageOpenWith = true,
                    ManageFileAssociation = true
                });
            }

            DialogResult = true;
        }
    }
}
