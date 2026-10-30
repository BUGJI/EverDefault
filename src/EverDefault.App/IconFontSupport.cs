using System;
using System.Windows.Media;

namespace EverDefault.App
{
    /// <summary>
    /// Detects whether the Segoe MDL2 Assets icon font is installed. It ships with
    /// Windows 10 and later; on Windows 7/8 the UI falls back to text labels.
    /// </summary>
    public static class IconFontSupport
    {
        public static bool IsAvailable { get; } = Detect();

        private static bool Detect()
        {
            try
            {
                foreach (var family in Fonts.SystemFontFamilies)
                {
                    var name = family.Source;
                    if (!string.IsNullOrEmpty(name)
                        && name.StartsWith("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception)
            {
                // Font enumeration is best-effort; treat failures as unsupported.
            }

            return false;
        }
    }
}
