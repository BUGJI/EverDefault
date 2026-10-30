using System;
using System.Windows;
using System.Windows.Media;
using EverDefault.Core.Model;
using Microsoft.Win32;
using Win32Registry = Microsoft.Win32.Registry;

namespace EverDefault.App
{
    /// <summary>Swaps the app's colour resource dictionary between light and dark.</summary>
    internal static class ThemeManager
    {
        private const string PersonalizeKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private const string DwmKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";

        private static bool _listening;
        private static bool _applying;

        public static ThemeMode Requested { get; private set; } = ThemeMode.System;

        public static bool IsDarkEffective
        {
            get
            {
                return Requested == ThemeMode.Dark
                       || (Requested == ThemeMode.System && !IsSystemLight());
            }
        }

        /// <summary>Starts listening for system theme/accent changes. Safe to call more than once.</summary>
        public static void Initialize()
        {
            if (_listening)
                return;

            _listening = true;
            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                EventManager.RegisterClassHandler(
                    typeof(Window),
                    FrameworkElement.LoadedEvent,
                    new RoutedEventHandler(OnWindowLoaded));
            }
            catch (Exception)
            {
                // Theme following is optional.
            }
        }

        public static void Apply(ThemeMode mode)
        {
            Requested = mode;
            var dark = IsDarkEffective;

            var app = Application.Current;
            if (app == null)
                return;

            if (_applying)
                return;

            _applying = true;
            try
            {
                var merged = app.Resources.MergedDictionaries;
                for (var i = merged.Count - 1; i >= 0; i--)
                {
                    var source = merged[i].Source == null ? string.Empty : merged[i].Source.OriginalString;
                    if (source.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
                        || source.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
                        merged.RemoveAt(i);
                }

                var uri = new Uri(
                    dark
                        ? "pack://application:,,,/EverDefault.App;component/Themes/Dark.xaml"
                        : "pack://application:,,,/EverDefault.App;component/Themes/Light.xaml",
                    UriKind.Absolute);

                var theme = new ResourceDictionary { Source = uri };
                ApplySystemAccent(theme, dark);
                merged.Insert(0, theme);
            }
            finally
            {
                _applying = false;
            }

            ApplyToOpenWindows();
        }

        /// <summary>True when Windows is configured to use light app colours.</summary>
        public static bool IsSystemLight()
        {
            try
            {
                var value = Win32Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
                return Convert.ToInt32(value) != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void ApplyToOpenWindows()
        {
            var app = Application.Current;
            if (app == null)
                return;

            foreach (Window window in app.Windows)
                WindowChromeHelper.Apply(window);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var window = sender as Window;
            if (window != null)
                WindowChromeHelper.Apply(window);
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.VisualStyle)
                return;

            var app = Application.Current;
            if (app == null)
                return;

            app.Dispatcher.BeginInvoke(new Action(() => Apply(Requested)));
        }

        /// <summary>Derives the accent and its states from the current Windows accent colour.</summary>
        private static void ApplySystemAccent(ResourceDictionary theme, bool dark)
        {
            var accent = TryGetSystemAccent();
            if (!accent.HasValue)
                return;

            var baseColor = accent.Value;
            var background = dark
                ? Color.FromRgb(0x20, 0x20, 0x20)
                : Color.FromRgb(0xF3, 0xF3, 0xF3);

            theme["AccentBrush"] = Freeze(baseColor);
            theme["AccentHoverBrush"] = Freeze(Blend(baseColor, Colors.White, dark ? 0.16 : 0.10));
            theme["AccentPressedBrush"] = Freeze(Blend(baseColor, Colors.Black, 0.18));
            theme["AccentSubtleBrush"] = Freeze(Blend(baseColor, background, 0.86));
            theme[SystemColors.HighlightBrushKey] = Freeze(baseColor);
        }

        private static Color? TryGetSystemAccent()
        {
            try
            {
                var value = Win32Registry.GetValue(DwmKey, "ColorizationColor", null);
                if (value == null)
                    return null;

                var argb = unchecked((uint)Convert.ToInt64(value));
                var red = (byte)((argb >> 16) & 0xFF);
                var green = (byte)((argb >> 8) & 0xFF);
                var blue = (byte)(argb & 0xFF);

                if (red + green + blue < 40)
                    return null;

                return Color.FromRgb(red, green, blue);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Color Blend(Color from, Color to, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromRgb(
                (byte)(from.R + (to.R - from.R) * amount),
                (byte)(from.G + (to.G - from.G) * amount),
                (byte)(from.B + (to.B - from.B) * amount));
        }

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
