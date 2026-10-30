using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EverDefault.App
{
    /// <summary>A small visual summary of what a protection will do.</summary>
    public partial class ProtectionPreview : UserControl
    {
        public ProtectionPreview()
        {
            InitializeComponent();
            GlyphText.Visibility = IconFontSupport.IsAvailable
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateIconBorder();
        }

        public void ShowDefaultApp(string appName, string appPath, IEnumerable<string> targets, string hint)
        {
            var items = (targets ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

            TitleText.Text = string.IsNullOrWhiteSpace(appName) ? "当前默认程序" : appName;
            DetailText.Text = items.Count == 0
                ? "还没有选择要保护的项目。"
                : "这些项目被其他程序改动时，会自动改回。";

            SetIcon(appPath, "\uE8B7");
            SetChips(items);
            HintText.Text = hint ?? string.Empty;
        }

        public void ShowMessage(string glyph, string title, string detail, string hint)
        {
            TitleText.Text = title;
            DetailText.Text = detail;
            HintText.Text = hint ?? string.Empty;
            SetGlyph(glyph);
            AppIconImage.Visibility = Visibility.Collapsed;
            UpdateIconBorder();
            ChipPanel.Children.Clear();
        }

        public void ShowSettings(string title, IEnumerable<KeyValuePair<string, string>> values, string hint)
        {
            TitleText.Text = title;
            DetailText.Text = "检测到修改时，会改回下面的值。";
            HintText.Text = hint ?? string.Empty;

            SetGlyph("\uE713");
            AppIconImage.Visibility = Visibility.Collapsed;
            UpdateIconBorder();

            ChipPanel.Children.Clear();
            foreach (var pair in values ?? Enumerable.Empty<KeyValuePair<string, string>>())
                AddChip(pair.Key + " = " + pair.Value, true);
        }

        private void SetIcon(string appPath, string fallbackGlyph)
        {
            var source = LoadIcon(appPath);
            if (source != null)
            {
                AppIconImage.Source = source;
                AppIconImage.Visibility = Visibility.Visible;
                GlyphText.Visibility = Visibility.Collapsed;
            }
            else
            {
                SetGlyph(fallbackGlyph);
                AppIconImage.Visibility = Visibility.Collapsed;
            }

            UpdateIconBorder();
        }

        private void SetGlyph(string glyph)
        {
            GlyphText.Text = string.IsNullOrEmpty(glyph) ? "\uE946" : glyph;
            GlyphText.Visibility = IconFontSupport.IsAvailable
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateIconBorder()
        {
            IconBorder.Visibility =
                GlyphText.Visibility == Visibility.Visible || AppIconImage.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void SetChips(IEnumerable<string> items)
        {
            ChipPanel.Children.Clear();
            foreach (var item in items)
                AddChip(item, false);
        }

        private void AddChip(string text, bool settings)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Background = (Brush)FindResource(settings ? "ControlBackgroundBrush" : "SurfaceAltBrush"),
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1)
            };

            border.Child = new TextBlock
            {
                Text = text,
                FontSize = (double)FindResource("FontSizeCaption"),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };

            ChipPanel.Children.Add(border);
        }

        private static ImageSource LoadIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null)
                        return null;

                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
