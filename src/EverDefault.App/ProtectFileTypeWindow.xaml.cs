using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using EverDefault.Core.Model;
using EverDefault.Registry.Associations;

namespace EverDefault.App
{
    /// <summary>One-click protection for a single extension using its current default app.</summary>
    public partial class ProtectFileTypeWindow : DialogWindow
    {
        private AssociationEntry _entry;

        public ProtectFileTypeWindow()
        {
            InitializeComponent();
            WindowSizing.Clamp(this);
            Loaded += (s, e) => ExtensionBox.Focus();
        }

        public List<DefaultAppRule> Rules { get; private set; }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateCurrent();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要保护的文件类型",
                Filter = "所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var extension = Path.GetExtension(dialog.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                CurrentText.Text = "这个文件没有扩展名。";
                return;
            }

            ExtensionBox.Text = extension;
        }

        private void UpdateCurrent()
        {
            var extension = AssociationCatalog.NormalizeExtension(ExtensionBox.Text);
            if (extension == null)
            {
                _entry = null;
                CurrentText.Text = string.Empty;
                ProtectButton.IsEnabled = false;
                Preview.ShowMessage("\uE8B7", "还没有选择文件类型",
                    "输入 .pdf 这样的扩展名，或点“选择文件...”。", string.Empty);
                return;
            }

            _entry = AssociationCatalog.Resolve(extension, AssociationKind.FileExtension);
            if (_entry == null)
            {
                CurrentText.Text = "这个文件类型还没有默认程序。";
                ProtectButton.IsEnabled = false;
                Preview.ShowMessage("\uE7BA", extension,
                    "没有读取到当前默认程序。可以到“高级配置”里手动指定。", string.Empty);
                return;
            }

            CurrentText.Text = "当前：" + _entry.AppName;
            ProtectButton.IsEnabled = true;
            Preview.ShowDefaultApp(
                _entry.AppName,
                _entry.AppPath,
                new List<string> { extension },
                "启用后不会改变现在的默认程序。");
        }

        private void OnProtect(object sender, RoutedEventArgs e)
        {
            var extension = AssociationCatalog.NormalizeExtension(ExtensionBox.Text);
            if (extension == null)
                return;

            Rules = ProtectionBuilder.BuildCurrentRules(
                new List<string> { extension }, new List<string>());

            if (Rules.Count == 0)
            {
                CurrentText.Text = "没有读取到可保护的默认程序。";
                return;
            }

            DialogResult = true;
        }
    }
}
