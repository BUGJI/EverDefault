using System.Windows;

namespace EverDefault.App
{
    /// <summary>
    /// Keeps windows inside the usable desktop area. At 150% scaling a 1120pt window is
    /// wider than a 1600px screen, so the predefined sizes are treated as maximums.
    /// </summary>
    internal static class WindowSizing
    {
        public static void Clamp(Window window)
        {
            if (window == null)
                return;

            var width = SystemParameters.VirtualScreenWidth;
            var height = SystemParameters.VirtualScreenHeight;
            window.MaxWidth = width;
            window.MaxHeight = height;

            if (window.MinWidth > width)
                window.MinWidth = width;

            if (window.MinHeight > height)
                window.MinHeight = height;

            if (!double.IsNaN(window.Width) && window.Width > width)
                window.Width = width;

            if (!double.IsNaN(window.Height) && window.Height > height)
                window.Height = height;
        }
    }
}
