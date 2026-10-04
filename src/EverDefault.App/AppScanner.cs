using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace EverDefault.App
{
    /// <summary>Shared app picker and format scanner used by the default-app authoring windows.</summary>
    internal static class AppScanner
    {
        public static List<AppInfo> LoadApplications()
        {
            return AppFormats.ListApplications();
        }

        public static AppInfo Browse(Window owner, IList<AppInfo> apps)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择应用程序",
                Filter = "应用程序 (*.exe)|*.exe|所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog(owner) != true)
                return null;

            return Ensure(apps, dialog.FileName);
        }

        public static AppInfo Ensure(IList<AppInfo> apps, string exePath)
        {
            var existing = apps.FirstOrDefault(
                a => string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            var fileName = Path.GetFileName(exePath);
            existing = new AppInfo
            {
                DisplayName = fileName + "  (手动选择)",
                ExeName = fileName,
                ExePath = exePath
            };
            apps.Add(existing);
            return existing;
        }

        public static Task<List<FormatGroup>> ScanAsync(AppInfo app)
        {
            var exeName = app.ExeName;
            var exePath = app.ExePath;
            return Task.Run(() => AppFormats.ScanFormats(exeName, exePath));
        }
    }
}
