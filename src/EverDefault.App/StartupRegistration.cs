using System;
using System.IO;
using Microsoft.Win32;

namespace EverDefault.App
{
    /// <summary>Registers/removes the app's per-user startup entry (HKCU\...\Run).</summary>
    internal static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "EverDefault";

        public static string AppExePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EverDefault.App.exe"); }
        }

        public static void Apply(bool runAtStartup, bool hideToTray)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                        return;

                    if (runAtStartup)
                    {
                        var value = "\"" + AppExePath + "\" --startup" + (hideToTray ? " --tray" : string.Empty);
                        key.SetValue(ValueName, value);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception)
            {
                // Non-fatal: startup registration is a convenience only.
            }
        }
    }
}
