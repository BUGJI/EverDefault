using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>Inline form for creating or editing a single rule of one module.</summary>
    public partial class RuleEditorPanel : UserControl
    {
        private static readonly Visibility Hidden = Visibility.Collapsed;
        private static readonly Visibility Shown = Visibility.Visible;

        private RuleModule _module = RuleModule.CustomRegistry;
        private Guid? _editingId;
        private RuleBase _editingBase;
        private bool _nameTouched;
        private bool _suppressName;
        private string _lastSuggestion = string.Empty;
        private string _appPath;
        private Dictionary<string, string> _progIdMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public RuleEditorPanel()
        {
            InitializeComponent();

            ModeBox.ItemsSource = Display.Options<RuleMode>(Display.Mode);
            ActionBox.ItemsSource = Display.Options<RuleAction>(Display.Action);
            NsMatchTypeBox.ItemsSource = Display.Options<NameSpaceMatchType>(Display.NsMatch);
            NsScopeBox.ItemsSource = Display.Options<NameSpaceDeleteScope>(Display.NsScope);
            MatchTypeBox.ItemsSource = Display.Options<ValueMatchType>(Display.ValueMatch);
            OnMismatchBox.ItemsSource = Display.Options<RuleAction>(Display.Action);

            Clear();

            NameBox.TextChanged += OnNameChanged;
            AppNameBox.TextChanged += OnFieldChanged;
            ExtensionsBox.TextChanged += OnFieldChanged;
            ProgIdBox.TextChanged += OnFieldChanged;
            NsPatternBox.TextChanged += OnFieldChanged;
            KeyPatternsBox.TextChanged += OnFieldChanged;
            ValueNameBox.TextChanged += OnFieldChanged;
        }

        public event EventHandler SaveRequested;

        public event EventHandler ResetRequested;

        public Guid? EditingId
        {
            get { return _editingId; }
        }

        public bool CanSave
        {
            get { return SaveButton.IsEnabled; }
            set { SaveButton.IsEnabled = value; }
        }

        public void Configure(RuleModule module)
        {
            _module = module;
            AppSection.Visibility = module == RuleModule.DefaultApp ? Shown : Hidden;
            NsSection.Visibility = module == RuleModule.NameSpace ? Shown : Hidden;
            CustomSection.Visibility = module == RuleModule.CustomRegistry ? Shown : Hidden;

            _nameTouched = false;
            RefreshAutoName();
        }

        public void Clear()
        {
            _suppressName = true;
            ResetFields();
            _suppressName = false;

            _editingId = null;
            _editingBase = null;
            _nameTouched = false;
            RefreshAutoName();
        }

        private void ResetFields()
        {
            NameBox.Text = string.Empty;
            ModeBox.SelectedValue = RuleMode.Monitor;
            ActionBox.SelectedValue = RuleAction.LogOnly;
            EnabledBox.IsChecked = true;
            IntervalBox.Text = "60";

            AppNameBox.Text = string.Empty;
            ExtensionsBox.Text = string.Empty;
            ProgIdBox.Text = string.Empty;
            OpenWithBox.IsChecked = true;
            FileAssocBox.IsChecked = true;
            _appPath = null;
            _progIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            UpdateAppMapHint();

            NsPathsBox.Text = string.Empty;
            NsMatchTypeBox.SelectedValue = NameSpaceMatchType.Guid;
            NsPatternBox.Text = string.Empty;
            NsScopeBox.SelectedValue = NameSpaceDeleteScope.NameSpaceOnly;
            RefreshShellBox.IsChecked = false;

            KeyPatternsBox.Text = string.Empty;
            ValueNameBox.Text = string.Empty;
            MatchTypeBox.SelectedValue = ValueMatchType.AnyChange;
            ExpectedKindBox.Text = string.Empty;
            ExpectedDataBox.Text = string.Empty;
            OnMismatchBox.SelectedValue = RuleAction.Restore;
        }

        public void LoadRule(RuleBase rule)
        {
            _suppressName = true;
            ResetFields();
            _editingId = null;
            _editingBase = null;

            if (rule != null)
            {
                _editingId = rule.Id;
                _editingBase = rule;
                NameBox.Text = rule.Name;
                ModeBox.SelectedValue = rule.Mode;
                ActionBox.SelectedValue = rule.Action;
                EnabledBox.IsChecked = rule.Enabled;
                IntervalBox.Text = rule.IntervalSeconds.ToString();

                var app = rule as DefaultAppRule;
                if (app != null)
                {
                    AppNameBox.Text = app.AppName;
                    ExtensionsBox.Text = string.Join(", ", app.Extensions);
                    ProgIdBox.Text = app.ProgId;
                    OpenWithBox.IsChecked = app.ManageOpenWith;
                    FileAssocBox.IsChecked = app.ManageFileAssociation;
                    _appPath = app.AppPath;
                    _progIdMap = app.ProgIdMap != null
                        ? new Dictionary<string, string>(app.ProgIdMap, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    UpdateAppMapHint();
                }

                var nameSpace = rule as NameSpaceRule;
                if (nameSpace != null)
                {
                    NsPathsBox.Text = string.Join(Environment.NewLine, nameSpace.PathPatterns);
                    NsMatchTypeBox.SelectedValue = nameSpace.MatchType;
                    NsPatternBox.Text = nameSpace.MatchPattern;
                    NsScopeBox.SelectedValue = nameSpace.DeleteScope;
                    RefreshShellBox.IsChecked = nameSpace.RefreshShell;
                }

                var custom = rule as CustomRegistryRule;
                if (custom != null)
                {
                    KeyPatternsBox.Text = string.Join(Environment.NewLine, custom.KeyPatterns);
                    ValueNameBox.Text = custom.ValueName;
                    MatchTypeBox.SelectedValue = custom.MatchType;
                    ExpectedKindBox.Text = custom.ExpectedKind;
                    ExpectedDataBox.Text = custom.ExpectedData;
                    OnMismatchBox.SelectedValue = custom.OnMismatch;
                }
            }

            _lastSuggestion = ComputeSuggestion();
            _nameTouched = rule != null;
            _suppressName = false;

            if (rule == null)
                RefreshAutoName();
        }

        private void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressName)
                return;

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                _nameTouched = false;
                RefreshAutoName();
                return;
            }

            _nameTouched = !string.Equals(NameBox.Text, _lastSuggestion, StringComparison.Ordinal);
        }

        private void OnFieldChanged(object sender, TextChangedEventArgs e)
        {
            RefreshAutoName();
        }

        private string ComputeSuggestion()
        {
            switch (_module)
            {
                case RuleModule.DefaultApp:
                    return RuleNaming.ForDefaultApp(
                        SplitList(ExtensionsBox.Text, true), Blank(ProgIdBox.Text), Blank(AppNameBox.Text));

                case RuleModule.NameSpace:
                    return RuleNaming.ForNameSpace(Blank(NsPatternBox.Text));

                default:
                    return RuleNaming.ForCustom(SplitList(KeyPatternsBox.Text, false), ValueNameBox.Text);
            }
        }

        private void RefreshAutoName()
        {
            if (_nameTouched || _suppressName)
                return;

            var suggestion = ComputeSuggestion();
            _lastSuggestion = suggestion;

            _suppressName = true;
            NameBox.Text = suggestion;
            try
            {
                NameBox.CaretIndex = NameBox.Text.Length;
            }
            catch (Exception)
            {
            }
            _suppressName = false;
        }

        public RuleBase BuildRule(out string error)
        {
            error = null;
            try
            {
                RuleBase rule;
                switch (_module)
                {
                    case RuleModule.DefaultApp:
                        rule = BuildDefaultApp();
                        break;

                    case RuleModule.NameSpace:
                        rule = BuildNameSpace();
                        break;

                    default:
                        rule = BuildCustom();
                        break;
                }

                rule.Name = Blank(NameBox.Text) ?? ComputeSuggestion();
                rule.Mode = (RuleMode)ModeBox.SelectedValue;
                rule.Action = (RuleAction)ActionBox.SelectedValue;
                rule.Enabled = EnabledBox.IsChecked == true;

                int interval;
                rule.IntervalSeconds = int.TryParse(IntervalBox.Text, out interval) && interval > 0 ? interval : 60;

                if (_editingBase != null)
                {
                    rule.Priority = _editingBase.Priority;
                    rule.MinOs = _editingBase.MinOs;
                    rule.MaxOs = _editingBase.MaxOs;
                    rule.CreatedUtc = _editingBase.CreatedUtc;
                }

                if (_editingId.HasValue)
                    rule.Id = _editingId.Value;

                return rule;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private DefaultAppRule BuildDefaultApp()
        {
            var extensions = new List<string>();
            foreach (var raw in SplitList(ExtensionsBox.Text, true))
            {
                var ext = DefaultAppRule.NormalizeExtension(raw);
                if (ext != null && !extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    extensions.Add(ext);
            }

            if (extensions.Count == 0)
                throw new InvalidOperationException("请至少填写一个文件扩展名（例如 .pdf）。");

            var progId = Blank(ProgIdBox.Text);

            var progIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in extensions)
            {
                string mapped;
                if (_progIdMap != null && _progIdMap.TryGetValue(ext, out mapped) && !string.IsNullOrWhiteSpace(mapped))
                    progIdMap[ext] = mapped.Trim();
            }

            if (progId == null && progIdMap.Count == 0)
                throw new InvalidOperationException("请填写要锁定的默认程序 ProgId（可点“选择...”从注册表选择，或用规则页的“按分组设置…”自动生成）。");

            if (progId == null)
            {
                var missing = extensions.Where(ext => !progIdMap.ContainsKey(ext)).ToList();
                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        "以下扩展名没有对应的 ProgId，请用“按分组设置…”重新生成，或为它们填写统一的 ProgId："
                        + string.Join(", ", missing));
            }

            return new DefaultAppRule
            {
                AppName = Blank(AppNameBox.Text),
                AppPath = _appPath,
                Extensions = extensions,
                ProgId = progId,
                ProgIdMap = progIdMap,
                ManageOpenWith = OpenWithBox.IsChecked == true,
                ManageFileAssociation = FileAssocBox.IsChecked == true
            };
        }

        private void UpdateAppMapHint()
        {
            if (AppMapHint != null)
                AppMapHint.Visibility = _progIdMap.Count > 0 ? Shown : Hidden;
        }

        private NameSpaceRule BuildNameSpace()
        {
            var paths = SplitList(NsPathsBox.Text, false);
            if (paths.Count == 0)
                throw new InvalidOperationException("请至少填写一条命名空间路径（可点“选择...”从注册表选择）。");

            var rule = new NameSpaceRule
            {
                PathPatterns = paths,
                MatchType = (NameSpaceMatchType)NsMatchTypeBox.SelectedValue,
                MatchPattern = Blank(NsPatternBox.Text),
                DeleteScope = (NameSpaceDeleteScope)NsScopeBox.SelectedValue,
                RefreshShell = RefreshShellBox.IsChecked == true
            };

            if (rule.MatchType != NameSpaceMatchType.Guid && rule.MatchPattern == null)
                throw new InvalidOperationException("匹配方式不是 GUID 时，需要填写“匹配内容”。");

            return rule;
        }

        private CustomRegistryRule BuildCustom()
        {
            var keys = SplitList(KeyPatternsBox.Text, false);
            if (keys.Count == 0)
                throw new InvalidOperationException("请至少填写一个注册表键（可点“从注册表选择...”）。");

            return new CustomRegistryRule
            {
                KeyPatterns = keys,
                ValueName = ValueNameBox.Text ?? string.Empty,
                MatchType = (ValueMatchType)MatchTypeBox.SelectedValue,
                ExpectedKind = Blank(ExpectedKindBox.Text),
                ExpectedData = Blank(ExpectedDataBox.Text),
                OnMismatch = (RuleAction)OnMismatchBox.SelectedValue
            };
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var handler = SaveRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            var handler = ResetRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void OnPickExtension(object sender, RoutedEventArgs e)
        {
            var picker = new RegistryPickerWindow(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts",
                true) { Owner = Window.GetWindow(this) };

            if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedKeyPath))
                return;

            var list = SplitList(ExtensionsBox.Text, true);
            if (!list.Any(x => string.Equals(x, picker.SelectedKeyPath, StringComparison.OrdinalIgnoreCase)))
                list.Add(picker.SelectedKeyPath);

            ExtensionsBox.Text = string.Join(", ", list);
        }

        private void OnPickProgId(object sender, RoutedEventArgs e)
        {
            var picker = new RegistryPickerWindow("HKEY_CLASSES_ROOT", true) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedKeyPath))
                ProgIdBox.Text = picker.SelectedKeyPath;
        }

        private void OnPickNameSpaceKey(object sender, RoutedEventArgs e)
        {
            var picker = new RegistryPickerWindow(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
                false) { Owner = Window.GetWindow(this) };

            if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedKeyPath))
                return;

            var list = SplitList(NsPathsBox.Text, false);
            if (!list.Any(x => string.Equals(x, picker.SelectedKeyPath, StringComparison.OrdinalIgnoreCase)))
                list.Add(picker.SelectedKeyPath);

            NsPathsBox.Text = string.Join(Environment.NewLine, list);
        }

        private void OnPickCustomKey(object sender, RoutedEventArgs e)
        {
            var picker = new RegistryPickerWindow(@"HKEY_CURRENT_USER\Software", false)
            {
                Owner = Window.GetWindow(this)
            };

            if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedKeyPath))
                return;

            KeyPatternsBox.Text = picker.SelectedKeyPath;

            if (picker.ValueChosen)
            {
                ValueNameBox.Text = picker.SelectedValueName ?? string.Empty;
                ExpectedKindBox.Text = picker.SelectedValueKind;
                ExpectedDataBox.Text = picker.SelectedValueData;
                MatchTypeBox.SelectedValue = ValueMatchType.Exact;
            }
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<string> SplitList(string text, bool commaSeparated)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var separators = commaSeparated ? new[] { ',', ';', '\n', '\r' } : new[] { '\n', '\r' };
            return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
        }
    }
}
