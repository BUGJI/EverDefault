using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    /// <summary>Inline form for creating or editing a single rule of one module.</summary>
    public partial class RuleEditorPanel : UserControl
    {
        private static readonly Visibility Hidden = Visibility.Collapsed;
        private static readonly Visibility Shown = Visibility.Visible;

        private readonly DispatcherTimer _statusTimer;
        private RuleModule _module = RuleModule.CustomRegistry;
        private Guid? _editingId;
        private RuleBase _editingBase;
        private bool _nameTouched;
        private bool _suppressName;
        private bool _loading;
        private bool _dirty;
        private bool _isNew;
        private bool _inlineMode;
        private bool _embedded;
        private bool _serviceAvailable;
        private string _lastSuggestion = string.Empty;
        private string _appPath;
        private Dictionary<string, string> _progIdMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public RuleEditorPanel()
        {
            InitializeComponent();

            ModeBox.ItemsSource = Display.Options<RuleMode>(Display.Mode);
            NsMatchTypeBox.ItemsSource = Display.Options<NameSpaceMatchType>(Display.NsMatch);
            NsScopeBox.ItemsSource = Display.Options<NameSpaceDeleteScope>(Display.NsScope);
            MatchTypeBox.ItemsSource = Display.Options<ValueMatchType>(Display.ValueMatch);
            OnMismatchBox.ItemsSource = Display.Options<RuleAction>(Display.Action);
            ExpectedKindBox.ItemsSource = new[] { "String", "DWord", "QWord", "MultiString", "Binary" };

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick += (sender, args) =>
            {
                _statusTimer.Stop();
                StatusText.Visibility = Hidden;
            };

            Clear();

            NameBox.TextChanged += OnNameChanged;
            AppNameBox.TextChanged += OnFieldChanged;
            ExtensionsBox.TextChanged += OnFieldChanged;
            ProtocolsBox.TextChanged += OnFieldChanged;
            ProgIdBox.TextChanged += OnFieldChanged;
            NsPatternBox.TextChanged += OnFieldChanged;
            KeyPatternsBox.TextChanged += OnFieldChanged;
            ValueNameBox.TextChanged += OnFieldChanged;

            Track(NameBox);
            Track(IntervalBox);
            Track(AppNameBox);
            Track(ExtensionsBox);
            Track(ProtocolsBox);
            Track(ProgIdBox);
            Track(ProtocolProgIdBox);
            Track(NsPathsBox);
            Track(NsPatternBox);
            Track(KeyPatternsBox);
            Track(ValueNameBox);
            Track(ExpectedKindBox);
            ExpectedKindBox.AddHandler(
                TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(OnEditedText));
            Track(ExpectedDataBox);
            Track(PolicyKeyBox);
            Track(ModeBox);
            Track(ActionBox);
            Track(NsMatchTypeBox);
            Track(NsScopeBox);
            Track(MatchTypeBox);
            Track(OnMismatchBox);
            Track(EnabledBox);
            Track(OpenWithBox);
            Track(FileAssocBox);
            Track(RefreshShellBox);
        }

        public event EventHandler SaveRequested;

        public event EventHandler ResetRequested;

        public Guid? EditingId
        {
            get { return _editingId; }
        }

        public bool IsDirty
        {
            get { return _dirty; }
        }

        public bool IsNewDraft
        {
            get { return _isNew; }
        }

        public bool HasDraft
        {
            get { return _isNew || _editingId.HasValue; }
        }

        public bool CanSave
        {
            get { return SaveButton.IsEnabled; }
            set
            {
                _serviceAvailable = value;
                UpdateSaveState();
            }
        }

        /// <summary>Hides the save/reset footer when hosted as the "advanced" tab.</summary>
        public void SetEmbedded(bool embedded)
        {
            _embedded = embedded;
            UpdateFooterVisibility();
        }

        /// <summary>
        /// In the main-window rule pages, an empty editor means "nothing selected" rather than
        /// "ready for a new rule"; the user clicks New to explicitly start a draft.
        /// </summary>
        public void SetInlineMode(bool inline)
        {
            _inlineMode = inline;
            UpdateEmptyState();
        }

        public void Configure(RuleModule module)
        {
            _module = module;
            _loading = true;
            try
            {
                AppSection.Visibility = module == RuleModule.DefaultApp ? Shown : Hidden;
                NsSection.Visibility = module == RuleModule.NameSpace ? Shown : Hidden;
                CustomSection.Visibility = module == RuleModule.CustomRegistry ? Shown : Hidden;

                ActionBox.ItemsSource = ActionOptions(module);
                if (!(ActionBox.SelectedValue is RuleAction))
                    ActionBox.SelectedValue = RuleAction.Restore;
            }
            finally
            {
                _loading = false;
            }

            _nameTouched = false;
            UpdateConditionalVisibility();
            UpdateKind();
            UpdateEmptyState();
        }

        /// <summary>
        /// "WritePolicy" is only meaningful for CustomRegistry, so hide it from the other modules'
        /// action dropdown. The custom editor shows a single action dropdown instead.
        /// </summary>
        private static List<EnumOption> ActionOptions(RuleModule module)
        {
            var options = Display.Options<RuleAction>(Display.Action);
            if (module == RuleModule.CustomRegistry)
                return options;

            return options
                .Where(o => !(o.Value is RuleAction) || (RuleAction)o.Value != RuleAction.WritePolicy)
                .ToList();
        }

        public void BeginNew()
        {
            _loading = true;
            _suppressName = true;
            try
            {
                ResetFields();
                _editingId = null;
                _editingBase = null;
                _isNew = true;
                _nameTouched = false;
            }
            finally
            {
                _suppressName = false;
                _loading = false;
            }

            _lastSuggestion = ComputeSuggestion();
            MarkClean();
            UpdateKind();
            UpdateConditionalVisibility();
            UpdateEmptyState();
            FocusName();
        }

        public void Clear()
        {
            _loading = true;
            _suppressName = true;
            try
            {
                ResetFields();
                _editingId = null;
                _editingBase = null;
                _isNew = false;
                _nameTouched = false;
            }
            finally
            {
                _suppressName = false;
                _loading = false;
            }

            _lastSuggestion = ComputeSuggestion();
            MarkClean();
            UpdateKind();
            UpdateConditionalVisibility();
            UpdateEmptyState();
        }

        public void FocusName()
        {
            if (NameBox.Visibility != Visibility.Visible)
                return;

            NameBox.Focus();
            NameBox.SelectAll();
        }

        public void ShowError(string error)
        {
            ValidationText.Text = string.IsNullOrWhiteSpace(error) ? "规则无效。" : error;
            ValidationText.Visibility = Shown;
        }

        public void ShowStatus(string message)
        {
            StatusText.Text = message ?? string.Empty;
            StatusText.Visibility = Shown;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        public void AcceptSaved(RuleBase rule)
        {
            if (rule == null)
                return;

            _editingId = rule.Id;
            _editingBase = rule;
            _isNew = false;
            _lastSuggestion = ComputeSuggestion();
            _nameTouched = true;
            MarkClean();
            UpdateKind();
            UpdateEmptyState();
        }

        private void ResetFields()
        {
            NameBox.Text = string.Empty;
            ModeBox.SelectedValue = RuleMode.Monitor;
            ActionBox.SelectedValue = RuleAction.Restore;
            EnabledBox.IsChecked = true;
            IntervalBox.Text = "60";

            AppNameBox.Text = string.Empty;
            ExtensionsBox.Text = string.Empty;
            ProtocolsBox.Text = string.Empty;
            ProgIdBox.Text = string.Empty;
            ProtocolProgIdBox.Text = string.Empty;
            OpenWithBox.IsChecked = true;
            FileAssocBox.IsChecked = true;
            ProgIdAdvanced.IsExpanded = false;
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
            PolicyKeyBox.Text = string.Empty;

            ValidationText.Text = string.Empty;
            ValidationText.Visibility = Hidden;
        }

        public void LoadRule(RuleBase rule)
        {
            if (rule == null)
            {
                Clear();
                return;
            }

            _loading = true;
            _suppressName = true;
            try
            {
                ResetFields();
                _editingId = rule.Id;
                _editingBase = rule;
                _isNew = false;

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
                    ProtocolsBox.Text = string.Join(", ", app.Protocols);
                    ProgIdBox.Text = app.ProgId;
                    ProtocolProgIdBox.Text = app.ProtocolProgId;
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
                    OnMismatchBox.SelectedValue = custom.OnMismatch != RuleAction.Restore
                        ? custom.OnMismatch
                        : custom.Action;
                    PolicyKeyBox.Text = custom.PolicyKey;
                }

                _lastSuggestion = ComputeSuggestion();
                _nameTouched = true;
            }
            finally
            {
                _suppressName = false;
                _loading = false;
            }

            MarkClean();
            UpdateKind();
            UpdateConditionalVisibility();
            UpdateEmptyState();
            ProgIdAdvanced.IsExpanded = !string.IsNullOrWhiteSpace(ProgIdBox.Text)
                                        || !string.IsNullOrWhiteSpace(ProtocolProgIdBox.Text);
        }

        private void Track(Control control)
        {
            var textBox = control as TextBox;
            if (textBox != null)
            {
                textBox.TextChanged += OnEdited;
                return;
            }

            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.SelectionChanged += OnEdited;
                return;
            }

            var check = control as CheckBox;
            if (check != null)
            {
                check.Checked += OnEdited;
                check.Unchecked += OnEdited;
            }
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

        private void OnEditedText(object sender, TextChangedEventArgs e)
        {
            OnEdited(sender, e);
        }

        private void OnEdited(object sender, EventArgs e)
        {
            if (_loading)
                return;

            MarkDirty();
            UpdateConditionalVisibility();
            UpdateSaveState();

            if (_module == RuleModule.DefaultApp)
            {
                var needsProtocol = SplitList(ProtocolsBox.Text, true).Count > 0
                                    && string.IsNullOrWhiteSpace(ProtocolProgIdBox.Text);
                var needsProgId = SplitList(ExtensionsBox.Text, true).Count > 0
                                 && string.IsNullOrWhiteSpace(ProgIdBox.Text)
                                 && _progIdMap.Count == 0;
                if (needsProtocol || needsProgId)
                    ProgIdAdvanced.IsExpanded = true;
            }

            var error = SoftValidation();
            ValidationText.Text = error ?? string.Empty;
            ValidationText.Visibility = error == null ? Hidden : Shown;
        }

        private string SoftValidation()
        {
            if (ModeBox.SelectedValue is RuleMode
                && (RuleMode)ModeBox.SelectedValue == RuleMode.Schedule)
            {
                int interval;
                if (!int.TryParse(IntervalBox.Text, out interval) || interval <= 0)
                    return "定时轮询秒数需要大于 0。";
            }

            if (_module == RuleModule.CustomRegistry)
            {
                var action = OnMismatchBox.SelectedValue is RuleAction
                    ? (RuleAction)OnMismatchBox.SelectedValue
                    : RuleAction.Restore;
                if (action == RuleAction.WritePolicy && string.IsNullOrWhiteSpace(PolicyKeyBox.Text))
                    return "选择“写策略”时需要填写“策略键”。";
            }

            if (_module == RuleModule.NameSpace)
            {
                var matchType = NsMatchTypeBox.SelectedValue is NameSpaceMatchType
                    ? (NameSpaceMatchType)NsMatchTypeBox.SelectedValue
                    : NameSpaceMatchType.Guid;
                if (matchType != NameSpaceMatchType.Guid && string.IsNullOrWhiteSpace(NsPatternBox.Text))
                    return "匹配方式不是 GUID 时，需要填写“匹配内容”。";
            }

            if (_module == RuleModule.DefaultApp)
            {
                if (SplitList(ProtocolsBox.Text, true).Count > 0
                    && string.IsNullOrWhiteSpace(ProtocolProgIdBox.Text))
                {
                    return "已填写 URL 协议，请同时填写“协议 ProgId”。";
                }

                if (SplitList(ExtensionsBox.Text, true).Count > 0
                    && string.IsNullOrWhiteSpace(ProgIdBox.Text)
                    && _progIdMap.Count == 0)
                {
                    return "请填写 ProgId，或使用“更多 → 按分组设置…”自动生成。";
                }
            }

            return null;
        }

        private void UpdateSaveState()
        {
            SaveButton.IsEnabled = _serviceAvailable && SoftValidation() == null;
        }

        private void MarkDirty()
        {
            if (_dirty)
                return;

            _dirty = true;
            DirtyText.Visibility = Shown;
        }

        private void MarkClean()
        {
            _dirty = false;
            DirtyText.Visibility = Hidden;
            ValidationText.Text = string.Empty;
            ValidationText.Visibility = Hidden;
            UpdateSaveState();
        }

        private string ComputeSuggestion()
        {
            switch (_module)
            {
                case RuleModule.DefaultApp:
                    return RuleNaming.ForDefaultApp(
                        SplitList(ExtensionsBox.Text, true),
                        Blank(ProgIdBox.Text) ?? Blank(ProtocolProgIdBox.Text),
                        Blank(AppNameBox.Text),
                        SplitList(ProtocolsBox.Text, true));

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

        private void UpdateConditionalVisibility()
        {
            var mode = ModeBox.SelectedValue is RuleMode ? (RuleMode)ModeBox.SelectedValue : RuleMode.Monitor;
            IntervalRow.Visibility = mode == RuleMode.Schedule ? Shown : Hidden;
            IntervalHint.Visibility = mode == RuleMode.Schedule ? Shown : Hidden;

            var custom = _module == RuleModule.CustomRegistry;
            ActionRow.Visibility = custom ? Hidden : Shown;
            OnMismatchRow.Visibility = custom ? Shown : Hidden;

            var action = OnMismatchBox.SelectedValue is RuleAction
                ? (RuleAction)OnMismatchBox.SelectedValue
                : RuleAction.Restore;
            PolicyKeyRow.Visibility = custom && action == RuleAction.WritePolicy ? Shown : Hidden;

            var matchType = NsMatchTypeBox.SelectedValue is NameSpaceMatchType
                ? (NameSpaceMatchType)NsMatchTypeBox.SelectedValue
                : NameSpaceMatchType.Guid;
            NsPatternRow.Visibility = _module == RuleModule.NameSpace && matchType != NameSpaceMatchType.Guid
                ? Shown
                : Hidden;
        }

        private void UpdateKind()
        {
            var module = Display.Module(_module);
            EditorKindText.Text = _isNew
                ? "新建" + module + "规则"
                : _editingId.HasValue ? "编辑" + module + "规则" : "未选择规则";
        }

        private void UpdateEmptyState()
        {
            var showForm = !_inlineMode || _isNew || _editingId.HasValue;
            FormScroll.Visibility = showForm ? Shown : Hidden;
            EmptyState.Visibility = showForm ? Hidden : Shown;
            UpdateFooterVisibility();
        }

        private void UpdateFooterVisibility()
        {
            var showForm = !_inlineMode || _isNew || _editingId.HasValue;
            FooterPanel.Visibility = _embedded || !showForm ? Hidden : Shown;
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
                rule.Mode = ModeBox.SelectedValue is RuleMode ? (RuleMode)ModeBox.SelectedValue : RuleMode.Monitor;
                rule.Action = ActionBox.SelectedValue is RuleAction ? (RuleAction)ActionBox.SelectedValue : RuleAction.LogOnly;
                rule.Enabled = EnabledBox.IsChecked == true;

                if (rule is CustomRegistryRule && OnMismatchBox.SelectedValue is RuleAction)
                    rule.Action = (RuleAction)OnMismatchBox.SelectedValue;

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

            var protocols = new List<string>();
            foreach (var raw in SplitList(ProtocolsBox.Text, true))
            {
                var protocol = DefaultAppRule.NormalizeProtocol(raw);
                if (protocol != null && !protocols.Contains(protocol, StringComparer.OrdinalIgnoreCase))
                    protocols.Add(protocol);
            }

            var protocolProgId = Blank(ProtocolProgIdBox.Text);

            if (extensions.Count == 0 && protocols.Count == 0)
                throw new InvalidOperationException("请至少填写一个文件扩展名（例如 .pdf）或一个 URL 协议（例如 http）。");

            var progId = Blank(ProgIdBox.Text);

            var progIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in extensions)
            {
                string mapped;
                if (_progIdMap != null && _progIdMap.TryGetValue(ext, out mapped) && !string.IsNullOrWhiteSpace(mapped))
                    progIdMap[ext] = mapped.Trim();
            }

            if (extensions.Count > 0)
            {
                if (progId == null && progIdMap.Count == 0)
                    throw new InvalidOperationException("请填写要锁定的默认程序 ProgId（可在“高级：手动指定 ProgId”中填写，或用“更多 → 按分组设置…”自动生成）。");

                if (progId == null)
                {
                    var missing = extensions.Where(ext => !progIdMap.ContainsKey(ext)).ToList();
                    if (missing.Count > 0)
                        throw new InvalidOperationException(
                            "以下扩展名没有对应的 ProgId，请用“更多 → 按分组设置…”重新生成，或为它们填写统一的 ProgId："
                            + string.Join(", ", missing));
                }
            }

            if (protocols.Count > 0 && protocolProgId == null)
                throw new InvalidOperationException("已填写 URL 协议，请同时填写“协议 ProgId”（在“高级：手动指定 ProgId”中）。");

            return new DefaultAppRule
            {
                AppName = Blank(AppNameBox.Text),
                AppPath = _appPath,
                Extensions = extensions,
                Protocols = protocols,
                ProgId = progId,
                ProtocolProgId = protocolProgId,
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

            var onMismatch = OnMismatchBox.SelectedValue is RuleAction
                ? (RuleAction)OnMismatchBox.SelectedValue
                : RuleAction.Restore;
            var policyKey = Blank(PolicyKeyBox.Text);

            if (onMismatch == RuleAction.WritePolicy && string.IsNullOrWhiteSpace(policyKey))
                throw new InvalidOperationException("选择“写策略”时需要填写“策略键”。");

            return new CustomRegistryRule
            {
                KeyPatterns = keys,
                ValueName = ValueNameBox.Text ?? string.Empty,
                MatchType = (ValueMatchType)MatchTypeBox.SelectedValue,
                ExpectedKind = Blank(ExpectedKindBox.Text),
                ExpectedData = Blank(ExpectedDataBox.Text),
                OnMismatch = onMismatch,
                PolicyKey = policyKey
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
