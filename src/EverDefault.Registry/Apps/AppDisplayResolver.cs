using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Win32Registry = Microsoft.Win32.Registry;

namespace EverDefault.Registry.Apps
{
    /// <summary>A ProgId resolved to a human-facing application name and executable.</summary>
    public sealed class AppDisplayInfo
    {
        public string ProgId { get; set; }

        public string Name { get; set; }

        public string ExePath { get; set; }
    }

    /// <summary>
    /// Resolves ProgIds to application display names without requiring the UI to know
    /// anything about the registry layout. Best-effort: falls back to the ProgId.
    /// </summary>
    public static class AppDisplayResolver
    {
        public static AppDisplayInfo FromProgId(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId))
                return new AppDisplayInfo { Name = "未知程序" };

            var trimmed = progId.Trim();
            var exe = ResolveExe(trimmed);

            return new AppDisplayInfo
            {
                ProgId = trimmed,
                ExePath = exe,
                Name = FriendlyName(trimmed, exe)
            };
        }

        public static string ResolveExe(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId))
                return null;

            var command = ReadCommand(@"HKEY_CLASSES_ROOT\" + progId + @"\shell\open\command");
            if (command == null)
                command = ReadCommand(@"HKEY_CURRENT_USER\Software\Classes\" + progId + @"\shell\open\command");
            if (command == null)
                command = ReadCommand(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\" + progId + @"\shell\open\command");

            return ExtractExe(command);
        }

        public static string FriendlyName(string progId, string exePath)
        {
            var exeName = FileName(exePath);

            if (!string.IsNullOrEmpty(exeName))
            {
                var friendly = ReadValue(@"HKEY_CLASSES_ROOT\Applications\" + exeName, "FriendlyAppName");
                if (IsUsableName(friendly))
                    return friendly.Trim();

                try
                {
                    var info = FileVersionInfo.GetVersionInfo(exePath);
                    if (IsUsableName(info.FileDescription))
                        return info.FileDescription.Trim();
                    if (IsUsableName(info.ProductName))
                        return info.ProductName.Trim();
                }
                catch (Exception)
                {
                    // The executable may not exist anymore; fall through.
                }

                return Path.GetFileNameWithoutExtension(exeName);
            }

            if (string.IsNullOrWhiteSpace(progId))
                return "未知程序";

            // Packaged (Store/UWP) handlers use hashed ProgIds that mean nothing to users.
            if (progId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase))
                return "系统应用";

            var dot = progId.IndexOf('.');
            return dot > 0 ? progId.Substring(0, dot) : progId;
        }

        /// <summary>Extracts the executable path from a shell command line.</summary>
        public static string ExtractExe(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            string text;
            try
            {
                text = Environment.ExpandEnvironmentVariables(command).Trim();
            }
            catch (Exception)
            {
                text = command.Trim();
            }

            if (text.Length == 0)
                return null;

            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                if (end > 1)
                    return text.Substring(1, end - 1);
            }

            var space = text.IndexOf(' ');
            return space < 0 ? text : text.Substring(0, space);
        }

        private static bool IsUsableName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith("@");
        }

        private static string FileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Path.GetFileName(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadCommand(string keyPath)
        {
            var value = ReadValue(keyPath, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string ReadValue(string keyPath, string valueName)
        {
            try
            {
                using (var key = OpenKey(keyPath))
                {
                    if (key == null)
                        return null;

                    var value = key.GetValue(valueName, null) as string;
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static RegistryKey OpenKey(string keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath))
                return null;

            const string currentUser = @"HKEY_CURRENT_USER\";
            const string localMachine = @"HKEY_LOCAL_MACHINE\";
            const string classesRoot = @"HKEY_CLASSES_ROOT\";
            const string users = @"HKEY_USERS\";

            try
            {
                if (keyPath.StartsWith(currentUser, StringComparison.OrdinalIgnoreCase))
                    return Win32Registry.CurrentUser.OpenSubKey(keyPath.Substring(currentUser.Length));
                if (keyPath.StartsWith(localMachine, StringComparison.OrdinalIgnoreCase))
                    return Win32Registry.LocalMachine.OpenSubKey(keyPath.Substring(localMachine.Length));
                if (keyPath.StartsWith(classesRoot, StringComparison.OrdinalIgnoreCase))
                    return Win32Registry.ClassesRoot.OpenSubKey(keyPath.Substring(classesRoot.Length));
                if (keyPath.StartsWith(users, StringComparison.OrdinalIgnoreCase))
                    return Win32Registry.Users.OpenSubKey(keyPath.Substring(users.Length));

                return Win32Registry.CurrentUser.OpenSubKey(keyPath);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
