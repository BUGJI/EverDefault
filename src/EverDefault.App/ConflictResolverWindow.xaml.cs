using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    public sealed class ConflictRow
    {
        public ConflictRow(RuleBase rule)
        {
            Rule = rule;
        }

        public RuleBase Rule { get; private set; }

        public string Title
        {
            get { return string.IsNullOrWhiteSpace(Rule.Name) ? "未命名保护" : Rule.Name; }
        }

        public string Detail
        {
            get
            {
                var module = Rule.Module == RuleModule.DefaultApp ? "默认应用"
                    : Rule.Module == RuleModule.NameSpace ? "此电脑" : "系统设置";
                return module + " · " + (Rule.Enabled ? "启用中" : "已停用");
            }
        }
    }

    /// <summary>Lets the user disable one side of a conflicting pair.</summary>
    public partial class ConflictResolverWindow : DialogWindow
    {
        private readonly IAppHost _host;

        public ConflictResolverWindow(IAppHost host, IEnumerable<RuleBase> rules, IEnumerable<Guid> ids)
        {
            _host = host;
            InitializeComponent();
            WindowSizing.Clamp(this);

            var wanted = new HashSet<Guid>(ids ?? Enumerable.Empty<Guid>());
            var rows = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => wanted.Contains(r.Id))
                .Select(r => new ConflictRow(r))
                .ToList();

            RuleList.ItemsSource = rows;
        }

        private async void OnDisable(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var row = button == null ? null : button.DataContext as ConflictRow;
            if (row == null)
                return;

            row.Rule.Enabled = false;
            var response = await _host.SendAsync(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(row.Rule));
            if (!response.Success)
            {
                _host.ShowWarning("停用失败：" + response.Error);
                return;
            }

            var rows = (RuleList.ItemsSource as IEnumerable<ConflictRow>)
                .Where(r => !ReferenceEquals(r, row))
                .ToList();
            RuleList.ItemsSource = rows;

            if (rows.Count == 0)
                DialogResult = true;
        }
    }
}
