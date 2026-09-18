using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EverDefault.App
{
    public partial class RegistryPickerWindow : Window
    {
        private const int FirstBatch = 200;
        private const int BatchSize = 150;
        private const int MaxValues = 2000;
        private const double LoadAheadPixels = 200;

        private static readonly Dictionary<string, RegistryKey> Bases =
            new Dictionary<string, RegistryKey>(StringComparer.OrdinalIgnoreCase)
            {
                { "HKEY_CURRENT_USER", Registry.CurrentUser },
                { "HKEY_LOCAL_MACHINE", Registry.LocalMachine },
                { "HKEY_CLASSES_ROOT", Registry.ClassesRoot },
                { "HKEY_USERS", Registry.Users },
                { "HKEY_CURRENT_CONFIG", Registry.CurrentConfig }
            };

        private readonly bool _leafNameOnly;
        private readonly HashSet<string> _loaded =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NodeState> _nodes =
            new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);
        private string _filter = string.Empty;
        private int _valueLoadToken;

        public RegistryPickerWindow(string startPath, bool leafNameOnly)
        {
            _leafNameOnly = leafNameOnly;
            InitializeComponent();
            LoadRoots();

            Loaded += (s, e) =>
            {
                var scroll = FindScrollViewer(Tree);
                if (scroll != null)
                    scroll.ScrollChanged += (sender, args) => LoadMoreVisible();
            };

            if (!string.IsNullOrEmpty(startPath))
                SelectPath(startPath);
        }

        public string SelectedKeyPath { get; private set; }

        public string SelectedValueName { get; private set; }

        public string SelectedValueKind { get; private set; }

        public string SelectedValueData { get; private set; }

        public bool ValueChosen { get; private set; }

        private void LoadRoots()
        {
            foreach (var name in Bases.Keys)
            {
                var item = new TreeViewItem { Header = name, Tag = name };
                item.Items.Add(new TreeViewItem());
                Tree.Items.Add(item);
            }
        }

        private async void OnItemExpanded(object sender, RoutedEventArgs e)
        {
            var item = e.OriginalSource as TreeViewItem;
            if (item == null)
                return;

            e.Handled = true;
            await PopulateChildrenAsync(item, false);
        }

        private async void OnApplyFilter(object sender, RoutedEventArgs e)
        {
            await ApplyFilterAsync();
        }

        private async void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ApplyFilterAsync();
            }
        }

        private async Task ApplyFilterAsync()
        {
            var item = Tree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null)
            {
                MessageBox.Show(this, "请先在左侧选择一个键，再筛选其子键。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _filter = (FilterBox.Text ?? string.Empty).Trim();
            await PopulateChildrenAsync(item, true);
        }

        private async Task PopulateChildrenAsync(TreeViewItem item, bool reload)
        {
            var path = item.Tag as string;
            if (string.IsNullOrEmpty(path))
                return;

            if (!reload && !_loaded.Add(path))
                return;

            _loaded.Add(path);

            var filter = _filter;
            SubKeyPage page;
            try
            {
                page = await Task.Run(() => EnumerateSubKeys(path, filter));
            }
            catch (Exception)
            {
                page = new SubKeyPage();
            }

            if (reload)
                item.Items.Clear();
            else
                RemovePlaceholders(item);

            var state = new NodeState
            {
                Path = path,
                Item = item,
                Names = page.Names.ToArray(),
                Total = page.Total
            };
            _nodes[path] = state;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in item.Items)
            {
                var tag = (child as TreeViewItem)?.Tag as string;
                if (tag != null)
                    existing.Add(tag);
            }

            AppendBatch(state, FirstBatch, existing);
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(LoadMoreVisible));
        }

        private void AppendBatch(NodeState state, int count, HashSet<string> existing)
        {
            if (state.Sentinel != null)
            {
                state.Item.Items.Remove(state.Sentinel);
                state.Sentinel = null;
            }

            var end = Math.Min(state.Loaded + count, state.Names.Length);
            for (; state.Loaded < end; state.Loaded++)
            {
                var childPath = state.Path + "\\" + state.Names[state.Loaded];
                if (existing != null && !existing.Add(childPath))
                    continue;

                var child = new TreeViewItem { Header = state.Names[state.Loaded], Tag = childPath };
                child.Items.Add(new TreeViewItem());
                state.Item.Items.Add(child);
            }

            if (state.Loaded < state.Names.Length)
            {
                state.Sentinel = new TreeViewItem
                {
                    Header = string.Format("滚动加载更多…（{0}/{1}）", state.Loaded, state.Names.Length),
                    Foreground = Brushes.Gray,
                    Focusable = false
                };
                state.Item.Items.Add(state.Sentinel);
            }
        }

        private void LoadMoreVisible()
        {
            for (var guard = 0; guard < 40; guard++)
            {
                var target = FindVisiblePending();
                if (target == null)
                    return;

                AppendBatch(target, BatchSize, null);
            }
        }

        private NodeState FindVisiblePending()
        {
            NodeState best = null;
            var bestY = double.MaxValue;

            foreach (var state in _nodes.Values)
            {
                if (state.Sentinel == null || state.Loaded >= state.Names.Length)
                    continue;

                double y;
                try
                {
                    y = state.Sentinel.TransformToAncestor(Tree).Transform(new Point(0, 0)).Y;
                }
                catch (InvalidOperationException)
                {
                    continue; // collapsed or not realized
                }

                if (y <= Tree.ActualHeight + LoadAheadPixels && y < bestY)
                {
                    bestY = y;
                    best = state;
                }
            }

            return best;
        }

        private static void RemovePlaceholders(TreeViewItem item)
        {
            for (var i = item.Items.Count - 1; i >= 0; i--)
            {
                var child = item.Items[i] as TreeViewItem;
                if (child == null || child.Tag == null)
                    item.Items.RemoveAt(i);
            }
        }

        private static SubKeyPage EnumerateSubKeys(string path, string filter)
        {
            var page = new SubKeyPage();

            RegistryKey key;
            bool owned;
            if (!TryOpenKey(path, out key, out owned))
                return page;

            try
            {
                var names = key.GetSubKeyNames();
                page.Total = names.Length;

                var hasFilter = !string.IsNullOrEmpty(filter);
                foreach (var name in names)
                {
                    if (hasFilter && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    page.Names.Add(name);
                }
            }
            catch (Exception)
            {
                // access denied on protected keys - show what we can
            }
            finally
            {
                if (owned)
                    key.Dispose();
            }

            return page;
        }

        private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = Tree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null)
                return;

            var path = item.Tag as string;
            PathText.Text = path;

            var token = ++_valueLoadToken;
            var rows = await Task.Run(() => ReadValues(path));
            if (token != _valueLoadToken)
                return;

            Values.ItemsSource = rows;
        }

        private static List<PickerValueRow> ReadValues(string path)
        {
            var rows = new List<PickerValueRow>();

            RegistryKey key;
            bool owned;
            if (!TryOpenKey(path, out key, out owned))
                return rows;

            try
            {
                foreach (var name in key.GetValueNames())
                {
                    var kind = key.GetValueKind(name);
                    var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    rows.Add(new PickerValueRow
                    {
                        Name = name,
                        Kind = kind.ToString(),
                        Data = Describe(raw, kind)
                    });

                    if (rows.Count >= MaxValues)
                        break;
                }
            }
            catch (Exception)
            {
                // ignore unreadable values
            }
            finally
            {
                if (owned)
                    key.Dispose();
            }

            return rows;
        }

        private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = Tree.SelectedItem as TreeViewItem;
            if (item == null)
                return;

            if (item.Tag == null)
            {
                LoadMoreVisible();
                return;
            }

            AcceptKey();
        }

        private void OnValueDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Values.SelectedItem != null)
                AcceptValue();
        }

        private void OnUseKey(object sender, RoutedEventArgs e)
        {
            AcceptKey();
        }

        private void OnUseValue(object sender, RoutedEventArgs e)
        {
            AcceptValue();
        }

        private void AcceptKey()
        {
            var path = PathText.Text;
            if (string.IsNullOrWhiteSpace(path))
                return;

            SelectedKeyPath = _leafNameOnly ? Leaf(path) : path;
            DialogResult = true;
        }

        private void AcceptValue()
        {
            var row = Values.SelectedItem as PickerValueRow;
            if (row == null)
            {
                MessageBox.Show(this, "请先在右上角选择一个值。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedKeyPath = _leafNameOnly ? Leaf(PathText.Text) : PathText.Text;
            SelectedValueName = row.Name;
            SelectedValueKind = row.Kind;
            SelectedValueData = row.Data;
            ValueChosen = true;
            DialogResult = true;
        }

        private void SelectPath(string fullPath)
        {
            var segments = fullPath.Split('\\');
            ItemsControl parent = Tree;
            TreeViewItem found = null;

            for (var i = 0; i < segments.Length; i++)
            {
                var partial = string.Join("\\", segments.Take(i + 1));
                found = FindItem(parent, partial);
                if (found == null)
                {
                    found = new TreeViewItem { Header = segments[i], Tag = partial };
                    found.Items.Add(new TreeViewItem());
                    parent.Items.Add(found);
                }

                found.IsExpanded = true;
                parent = found;
            }

            if (found != null)
            {
                found.IsSelected = true;
                found.BringIntoView();
            }
        }

        private static TreeViewItem FindItem(ItemsControl parent, string tagPath)
        {
            foreach (var child in parent.Items)
            {
                var item = child as TreeViewItem;
                if (item != null && string.Equals(item.Tag as string, tagPath, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer)
                return (ScrollViewer)root;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                    return found;
            }

            return null;
        }

        private static bool TryOpenKey(string fullPath, out RegistryKey key, out bool owned)
        {
            key = null;
            owned = false;

            if (string.IsNullOrEmpty(fullPath))
                return false;

            var slash = fullPath.IndexOf('\\');
            var root = slash < 0 ? fullPath : fullPath.Substring(0, slash);
            var sub = slash < 0 ? string.Empty : fullPath.Substring(slash + 1);

            RegistryKey baseKey;
            if (!Bases.TryGetValue(root, out baseKey))
                return false;

            if (string.IsNullOrEmpty(sub))
            {
                // Shared static roots must never be disposed by callers.
                key = baseKey;
                return true;
            }

            key = baseKey.OpenSubKey(sub, false);
            owned = key != null;
            return key != null;
        }

        private static string Leaf(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            var slash = path.LastIndexOf('\\');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        private static string Describe(object value, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return value as string;

                case RegistryValueKind.DWord:
                    return Convert.ToInt32(value).ToString();

                case RegistryValueKind.QWord:
                    return Convert.ToInt64(value).ToString();

                case RegistryValueKind.MultiString:
                    return string.Join("\n", (string[])value);

                case RegistryValueKind.Binary:
                    var bytes = (byte[])value;
                    return bytes.Length > 24
                        ? BitConverter.ToString(bytes, 0, 24) + "..."
                        : BitConverter.ToString(bytes);

                default:
                    return value == null ? string.Empty : value.ToString();
            }
        }

        private sealed class NodeState
        {
            public string Path;

            public TreeViewItem Item;

            public string[] Names;

            public int Total;

            public int Loaded;

            public TreeViewItem Sentinel;
        }
    }

    internal sealed class SubKeyPage
    {
        public List<string> Names { get; } = new List<string>();

        public int Total { get; set; }
    }

    public sealed class PickerValueRow
    {
        public string Name { get; set; }

        public string Kind { get; set; }

        public string Data { get; set; }
    }
}
