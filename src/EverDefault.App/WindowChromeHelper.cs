using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EverDefault.App
{
    /// <summary>
    /// Applies Windows 11 window chrome (dark title bar, rounded corners) to WPF windows.
    /// Every call is best-effort: unsupported Windows versions simply reject the attribute.
    /// </summary>
    internal static class WindowChromeHelper
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            if (window == null)
                return;

            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    return;

                var dark = ThemeManager.IsDarkEffective ? 1 : 0;
                if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                {
                    var legacy = dark;
                    DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref legacy, sizeof(int));
                }

                var corner = DwmWindowCornerRound;
                DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));
            }
            catch (Exception)
            {
                // Chrome is cosmetic; never let it break the window.
            }
        }
    }
}
