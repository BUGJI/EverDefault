using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using EverDefault.Core.Model;
using EverDefault.Registry.Associations;

namespace EverDefault.App
{
    public sealed class AdoptRow : INotifyPropertyChanged
    {
        private bool _selected;

        public AdoptRow(AssociationAppGroup group, int covered)
        {
            Group = group;
            Covered = covered;
            _selected = group.Entries.Count > covered;
        }

        public AssociationAppGroup Group { get; private set; }

        public int Covered { get; private set; }

        public string Title
        {
            get { return Group.AppName ?? "未知程序"; }
        }

        public string Summary
        {
            get { return Group.Summary; }
        }

        public string Note
        {
            get
            {
                if (Covered == 0)
                    return string.Empty;
                return "其中 " + Covered + " 项已经在保护中。";
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

    /// <summary>Reads the current defaults and turns selected applications into rules.</summary>
    public partial class AdoptDefaultsWindow : DialogWindow
    {
        private readonly List<RuleBase> _existing;
        private List<AdoptRow> _rows = new List<AdoptRow>();

        public AdoptDefaultsWindow(IEnumerable<RuleBase> existing)
        {
            _existing = (existing ?? Enumerable.Empty<RuleBase>()).ToList();
            InitializeComponent();
            WindowSizing.Clamp(this);
            LoadGroups();
        }

        public List<DefaultAppRule> Rules { get; private set; }

        private void LoadGroups()
        {
            var entries = AssociationCatalog.LoadCurrentUser();
            var groups = AssociationCatalog.GroupByApp(entries);

            _rows = groups.Select(group =>
            {
                var covered = group.Entries.Count(entry => ProtectionBuilder.IsCovered(_existing, entry));
                return new AdoptRow(group, covered);
            }).ToList();

            GroupList.ItemsSource = _rows;
            StatusText.Text = _rows.Count == 0
                ? "没有读取到当前默认程序。"
                : "找到 " + _rows.Count + " 个程序。";
            ApplyButton.IsEnabled = _rows.Any(r => r.IsSelected);
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsSelected = true;
            GroupList.Items.Refresh();
        }

        private void OnSelectNone(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsSelected = false;
            GroupList.Items.Refresh();
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            var selected = _rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "请至少勾选一个程序。";
                return;
            }

            var groups = new List<AssociationAppGroup>();
            foreach (var row in selected)
            {
                var filtered = new AssociationAppGroup
                {
                    AppName = row.Group.AppName,
                    AppPath = row.Group.AppPath
                };

                foreach (var entry in row.Group.Entries)
                {
                    if (!ProtectionBuilder.IsCovered(_existing, entry))
                        filtered.Entries.Add(entry);
                }

                if (filtered.Entries.Count > 0)
                    groups.Add(filtered);
            }

            Rules = ProtectionBuilder.BuildRulesFromAssociations(groups);
            if (Rules.Count == 0)
            {
                StatusText.Text = "这些项目已经在保护中，没有需要新增的内容。";
                return;
            }

            DialogResult = true;
        }
    }
}
