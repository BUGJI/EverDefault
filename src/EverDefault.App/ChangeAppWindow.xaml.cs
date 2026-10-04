using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>One extension and the ProgId resolved for the chosen application.</summary>
    public sealed class MappingRow
    {
        public string Extension { get; set; }

        public string ProgId { get; set; }

        public string Status
        {
            get { return string.IsNullOrEmpty(ProgId) ? "不支持" : ProgId; }
        }
    }

    /// <summary>
    /// Re-targets an existing group of extensions to a new application: pick the app,
    /// resolve each extension's ProgId from the registry, and return the new mapping.
    /// </summary>
    public partial class ChangeAppWindow : Window
    {
        private readonly List<string> _extensions;
        private readonly List<AppInfo> _apps = new List<AppInfo>();
        private List<MappingRow> _rows = new List<MappingRow>();

        public ChangeAppWindow(IList<string> extensions, string currentApp)
        {
            InitializeComponent();

            _extensions = (extensions ?? new List<string>())
                .Select(DefaultAppRule.NormalizeExtension)
                .Where(e => e != null)
                .Distinct()
                .ToList();

            HeaderText.Text = "为「" + (currentApp ?? "默认应用规则") + "」重新选择应用，共 " + _extensions.Count + " 个扩展名。";

            _apps.AddRange(AppScanner.LoadApplications());

            AppBox.ItemsSource = _apps;
            if (_apps.Count > 0)
                AppBox.SelectedIndex = 0;
        }

        public List<string> Extensions { get; private set; }

        public Dictionary<string, string> ProgIdMap { get; private set; }

        public string AppName { get; private set; }

        public string AppPath { get; private set; }

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
            ApplyButton.IsEnabled = false;
            PreviewList.ItemsSource = null;
            StatusText.Text = "正在扫描 " + app.ExeName + " ...";

            Dictionary<string, string> lookup;
            try
            {
                var scanned = await AppScanner.ScanAsync(app);
                lookup = AppFormats.FlattenByExtension(scanned);
            }
            catch (Exception ex)
            {
                StatusText.Text = "扫描失败：" + ex.Message;
                ScanButton.IsEnabled = true;
                return;
            }

            ScanButton.IsEnabled = true;

            _rows = new List<MappingRow>();
            var supported = 0;
            foreach (var extension in _extensions)
            {
                string progId;
                lookup.TryGetValue(extension, out progId);
                if (!string.IsNullOrEmpty(progId))
                    supported++;

                _rows.Add(new MappingRow { Extension = extension, ProgId = progId });
            }

            PreviewList.ItemsSource = _rows;
            ApplyButton.IsEnabled = supported > 0;
            StatusText.Text = supported + "/" + _extensions.Count + " 个扩展名可绑定；标“不支持”的将跳过。";
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            var app = AppBox.SelectedItem as AppInfo;
            if (app == null)
            {
                StatusText.Text = "请先选择应用。";
                return;
            }

            var extensions = new List<string>();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                if (string.IsNullOrEmpty(row.ProgId))
                    continue;

                extensions.Add(row.Extension);
                map[row.Extension] = row.ProgId;
            }

            if (extensions.Count == 0)
            {
                StatusText.Text = "该应用没有为任何扩展名注册打开方式。";
                return;
            }

            Extensions = extensions;
            ProgIdMap = map;
            AppName = app.DisplayName;
            AppPath = app.ExePath;
            DialogResult = true;
        }
    }
}
