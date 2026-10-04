using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>A resolved plan for one format group: which extensions map to which ProgId.</summary>
    public sealed class GroupPlan
    {
        public FormatGroupPreset Group { get; set; }

        public List<string> Extensions { get; } = new List<string>();

        public List<string> Unsupported { get; } = new List<string>();

        public Dictionary<string, string> ProgIdMap { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasSupport
        {
            get { return Extensions.Count > 0; }
        }

        public string Header
        {
            get
            {
                var total = Extensions.Count + Unsupported.Count;
                return Group.Name + "（" + Extensions.Count + "/" + total + " 支持）";
            }
        }

        public string Detail
        {
            get
            {
                var mapped = string.Join("  ", Extensions.Select(ext => ext + " → " + ProgIdMap[ext]));
                if (Unsupported.Count > 0)
                    mapped += "    不支持: " + string.Join(", ", Unsupported);
                return mapped;
            }
        }
    }

    /// <summary>
    /// Format-group-centric batch apply: pick one or more common format groups plus one
    /// application, resolve the per-extension ProgId from the registry, and emit one
    /// grouped <see cref="DefaultAppRule"/> per selected group.
    /// </summary>
    public partial class FormatGroupWindow : Window
    {
        private readonly List<AppInfo> _apps = new List<AppInfo>();
        private Dictionary<string, string> _extToProgId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<GroupPlan> _plans = new List<GroupPlan>();

        public FormatGroupWindow()
        {
            InitializeComponent();

            GroupList.ItemsSource = FormatGroups.All;

            _apps.AddRange(AppScanner.LoadApplications());

            AppBox.ItemsSource = _apps;
            if (_apps.Count > 0)
                AppBox.SelectedIndex = 0;
            if (FormatGroups.All.Count > 0)
                GroupList.SelectedIndex = 0;
        }

        /// <summary>Grouped rules to create, filled when the user confirms.</summary>
        public List<DefaultAppRule> Rules { get; private set; }

        private void OnSelectAllGroups(object sender, RoutedEventArgs e)
        {
            foreach (var group in FormatGroups.All)
            {
                if (!GroupList.SelectedItems.Contains(group))
                    GroupList.SelectedItems.Add(group);
            }
        }

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
            var groups = GroupList.SelectedItems.Cast<FormatGroupPreset>().ToList();
            if (groups.Count == 0)
            {
                StatusText.Text = "请先在左侧选择至少一个格式分组。";
                return;
            }

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

            List<FormatGroup> scanned;
            try
            {
                scanned = await AppScanner.ScanAsync(app);
            }
            catch (Exception ex)
            {
                StatusText.Text = "扫描失败：" + ex.Message;
                ScanButton.IsEnabled = true;
                return;
            }

            ScanButton.IsEnabled = true;
            _extToProgId = AppFormats.FlattenByExtension(scanned);
            _plans = BuildPlans(groups);

            PreviewList.ItemsSource = _plans;

            var supported = _plans.Sum(p => p.Extensions.Count);
            var total = _plans.Sum(p => p.Extensions.Count + p.Unsupported.Count);
            ApplyButton.IsEnabled = supported > 0;

            StatusText.Text = supported == 0
                ? "该应用没有为所选分组注册任何文件类型，请换一个应用或 exe。"
                : "共 " + supported + "/" + total + " 个扩展名可绑定；标“不支持”的将跳过。核对后点“生成规则”。";
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
            var progIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in _plans)
            {
                if (!plan.HasSupport)
                    continue;

                foreach (var extension in plan.Extensions)
                {
                    if (!extensions.Contains(extension))
                        extensions.Add(extension);

                    progIdMap[extension] = plan.ProgIdMap[extension];
                }
            }

            if (extensions.Count == 0)
            {
                StatusText.Text = "没有可生成的规则。";
                return;
            }

            Rules = new List<DefaultAppRule>
            {
                new DefaultAppRule
                {
                    Name = RuleNaming.ForDefaultApp(extensions, null, app.DisplayName),
                    AppName = app.DisplayName,
                    AppPath = app.ExePath,
                    Extensions = extensions,
                    ProgIdMap = progIdMap,
                    ManageOpenWith = true,
                    ManageFileAssociation = true
                }
            };

            DialogResult = true;
        }

        private List<GroupPlan> BuildPlans(List<FormatGroupPreset> groups)
        {
            var plans = new List<GroupPlan>();
            foreach (var group in groups)
            {
                var plan = new GroupPlan { Group = group };
                foreach (var raw in group.Extensions)
                {
                    var ext = DefaultAppRule.NormalizeExtension(raw);
                    if (ext == null)
                        continue;

                    string progId;
                    if (_extToProgId.TryGetValue(ext, out progId))
                    {
                        plan.Extensions.Add(ext);
                        plan.ProgIdMap[ext] = progId;
                    }
                    else
                    {
                        plan.Unsupported.Add(ext);
                    }
                }

                plans.Add(plan);
            }

            return plans;
        }
    }
}
