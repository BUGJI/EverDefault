using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    /// <summary>One rule category: the list on the left and an inline editor on the right.</summary>
    public partial class RuleModulePage : UserControl, IAppPage
    {
        private readonly IAppHost _host;
        private readonly RuleModule _module;
        private bool _serviceRunning;
        private Guid? _selectedId;
        private bool _suppressSelection;

        public RuleModulePage(IAppHost host, RuleModule module)
        {
            _host = host;
            _module = module;
            InitializeComponent();

            TitleText.Text = Display.Module(module);
            DescText.Text = Describe(module);
            TestButton.Visibility = module == RuleModule.CustomRegistry
                ? Visibility.Visible
                : Visibility.Collapsed;
            ImportFormatsButton.Visibility = module == RuleModule.DefaultApp
                ? Visibility.Visible
                : Visibility.Collapsed;

            Editor.Configure(module);
            Editor.SaveRequested += OnEditorSave;
            Editor.ResetRequested += OnEditorReset;

            UpdateButtons();
        }

        private static string Describe(RuleModule module)
        {
            switch (module)
            {
                case RuleModule.DefaultApp:
                    return "锁定文件类型的默认打开程序，被其他软件改动后自动还原。";
                case RuleModule.NameSpace:
                    return "检测并删除“此电脑/桌面”下多余的自定义命名空间项。";
                default:
                    return "按键模式监控任意注册表值，可按基线或期望值还原/删除/仅记录。";
            }
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;

            if (snapshot.Rules != null)
            {
                var rows = snapshot.Rules.Where(r => r.Rule.Module == _module).ToList();

                _suppressSelection = true;
                RulesList.ItemsSource = rows;
                if (_selectedId.HasValue)
                {
                    var match = rows.FirstOrDefault(r => r.Rule.Id == _selectedId.Value);
                    RulesList.SelectedItem = match;
                    if (match == null)
                    {
                        _selectedId = null;
                        Editor.Clear();
                    }
                }

                _suppressSelection = false;

                EmptyHint.Text = _serviceRunning && rows.Count == 0
                    ? "还没有规则。点『新建』创建第一条，或点『测试规则』先体验一次。"
                    : string.Empty;
            }
            else
            {
                EmptyHint.Text = _serviceRunning ? string.Empty : "后台服务未运行，无法读取规则。";
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            NewButton.IsEnabled = _serviceRunning;
            DeleteButton.IsEnabled = _serviceRunning && _selectedId.HasValue;
            TestButton.IsEnabled = _serviceRunning;
            ImportFormatsButton.IsEnabled = _serviceRunning;
            Editor.CanSave = _serviceRunning;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;

            var row = RulesList.SelectedItem as RuleRow;
            if (row == null)
                return;

            _selectedId = row.Rule.Id;
            Editor.LoadRule(row.Rule);
            UpdateButtons();
        }

        private void OnNew(object sender, RoutedEventArgs e)
        {
            _selectedId = null;
            _suppressSelection = true;
            RulesList.SelectedItem = null;
            _suppressSelection = false;

            Editor.Clear();
            UpdateButtons();
        }

        private async void OnDelete(object sender, RoutedEventArgs e)
        {
            var row = RulesList.SelectedItem as RuleRow;
            if (row == null)
            {
                _host.ShowInfo("请先选择要删除的规则。");
                return;
            }

            if (!_host.Confirm("删除规则 \"" + row.Name + "\"？删除后不再保护它的目标。"))
                return;

            var response = await _host.SendAsync(IpcProtocol.CommandDeleteRule, row.Rule.Id.ToString());
            if (!response.Success)
            {
                _host.ShowWarning("服务未运行，规则未删除：\n" + response.Error);
                return;
            }

            _selectedId = null;
            Editor.Clear();
            _host.Refresh();
        }

        private async void OnEditorSave(object sender, EventArgs e)
        {
            string error;
            var rule = Editor.BuildRule(out error);
            if (rule == null)
            {
                _host.ShowWarning("规则无效：" + error);
                return;
            }

            var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
            if (!response.Success)
            {
                _host.ShowWarning("服务未运行，规则未保存：\n" + response.Error);
                return;
            }

            _selectedId = rule.Id;
            _host.Refresh();
        }

        private void OnEditorReset(object sender, EventArgs e)
        {
            var row = RulesList.SelectedItem as RuleRow;
            if (row != null)
                Editor.LoadRule(row.Rule);
            else
                Editor.Clear();
        }

        private async void OnImportFormats(object sender, RoutedEventArgs e)
        {
            var window = new ApplicationFormatsWindow { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Rules == null || window.Rules.Count == 0)
                return;

            var succeeded = 0;
            var errors = new List<string>();
            foreach (var rule in window.Rules)
            {
                var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
                if (response.Success)
                    succeeded++;
                else
                    errors.Add(rule.Name + ": " + response.Error);
            }

            _host.Refresh();

            if (errors.Count > 0)
            {
                _host.ShowWarning("导入完成：成功 " + succeeded + " 条，失败 " + errors.Count + " 条。\n"
                    + string.Join("\n", errors.Take(8)));
            }
            else
            {
                _host.ShowInfo("已创建 " + succeeded + " 条默认应用规则。");
            }
        }

        private async void OnTestRule(object sender, RoutedEventArgs e)
        {
            var rule = new CustomRegistryRule
            {
                Name = "测试规则(仅记录)",
                Mode = RuleMode.Monitor,
                Enabled = true,
                Action = RuleAction.LogOnly,
                OnMismatch = RuleAction.LogOnly,
                MatchType = ValueMatchType.AnyChange,
                KeyPatterns = new List<string> { @"HKEY_CURRENT_USER\Software\EverDefaultTest" }
            };

            var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
            if (!response.Success)
            {
                _host.ShowWarning("服务未运行，无法创建测试规则：\n" + response.Error);
                return;
            }

            _host.ShowInfo(
                "已创建测试规则（仅记录，不改动）。\n\n" +
                "用 regedit 或命令行在下面这个键下新建/修改任意值，几秒后即可在“日志”页看到：\n" +
                @"HKEY_CURRENT_USER\Software\EverDefaultTest\");

            _selectedId = rule.Id;
            _host.Refresh();
        }
    }
}
