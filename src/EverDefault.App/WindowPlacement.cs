using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace EverDefault.App
{
    /// <summary>Remembers the main window's size and position between launches.</summary>
    internal static class WindowPlacement
    {
        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EverDefault",
                    "window.txt");
            }
        }

        public static void Restore(Window window)
        {
            if (window == null)
                return;

            try
            {
                if (!File.Exists(FilePath))
                    return;

                var parts = File.ReadAllText(FilePath).Split(',');
                if (parts.Length < 5)
                    return;

                double left, top, width, height;
                int state;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top)
                    || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
                    || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
                    || !int.TryParse(parts[4], out state))
                    return;

                if (width < 100 || height < 100)
                    return;

                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = left;
                window.Top = top;
                window.Width = Math.Min(width, window.MaxWidth);
                window.Height = Math.Min(height, window.MaxHeight);

                if (!IsVisibleOnDesktop(window))
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    window.Left = SystemParameters.WorkArea.Left
                                  + (SystemParameters.WorkArea.Width - window.Width) / 2;
                    window.Top = SystemParameters.WorkArea.Top
                                 + (SystemParameters.WorkArea.Height - window.Height) / 2;
                }

                if (state == 1)
                    window.WindowState = WindowState.Maximized;
            }
            catch (Exception)
            {
                // A broken placement file must not block startup.
            }
        }

        public static void Save(Window window)
        {
            if (window == null)
                return;

            try
            {
                var bounds = window.WindowState == WindowState.Normal
                    ? new Rect(window.Left, window.Top, window.Width, window.Height)
                    : window.RestoreBounds;

                if (bounds.Width < 100 || bounds.Height < 100)
                    return;

                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var value = string.Join(",", new[]
                {
                    bounds.Left.ToString("R", CultureInfo.InvariantCulture),
                    bounds.Top.ToString("R", CultureInfo.InvariantCulture),
                    bounds.Width.ToString("R", CultureInfo.InvariantCulture),
                    bounds.Height.ToString("R", CultureInfo.InvariantCulture),
                    window.WindowState == WindowState.Maximized ? "1" : "0"
                });

                File.WriteAllText(FilePath, value);
            }
            catch (Exception)
            {
                // Placement is a convenience only.
            }
        }

        private static bool IsVisibleOnDesktop(Window window)
        {
            var desktop = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            var bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            return desktop.IntersectsWith(bounds);
        }
    }
}
