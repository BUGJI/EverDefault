using System;
using System.Windows;
using EverDefault.Core.Model;
using Microsoft.Win32;

namespace EverDefault.App
{
    /// <summary>Swaps the app's colour resource dictionary between light and dark.</summary>
    internal static class ThemeManager
    {
        private const string PersonalizeKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static ThemeMode Requested { get; private set; } = ThemeMode.System;

        public static bool IsDarkEffective
        {
            get
            {
                return Requested == ThemeMode.Dark
                       || (Requested == ThemeMode.System && !IsSystemLight());
            }
        }

        public static void Apply(ThemeMode mode)
        {
            Requested = mode;
            var dark = IsDarkEffective;

            var app = Application.Current;
            if (app == null)
                return;

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
            merged.Insert(0, new ResourceDictionary { Source = uri });
        }

        /// <summary>True when Windows is configured to use light app colours.</summary>
        public static bool IsSystemLight()
        {
            try
            {
                var value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
                return Convert.ToInt32(value) != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
