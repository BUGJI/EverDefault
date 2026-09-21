using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace EverDefault.App
{
    /// <summary>
    /// Detects/starts the installed Windows service and can launch or stop the temporary
    /// console engine. Only the install/start-service actions require elevation.
    /// </summary>
    internal static class ServiceControl
    {
        public const string ServiceName = "EverDefault";

        private static Process _temporary;

        public static string ServiceExePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EverDefault.Service.exe"); }
        }

        public static bool IsInstalled()
        {
            return Run("sc.exe", "query " + ServiceName);
        }

        public static bool IsTemporaryRunning
        {
            get
            {
                try
                {
                    return _temporary != null && !_temporary.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        /// <summary>Attempts to start the installed service without elevation.</summary>
        public static bool TryStartInstalled()
        {
            return Run("sc.exe", "start " + ServiceName);
        }

        public static bool StartTemporary()
        {
            if (IsTemporaryRunning)
                return true;

            if (!File.Exists(ServiceExePath))
                return false;

            try
            {
                var args = "--console --parent " + Process.GetCurrentProcess().Id;
                _temporary = Process.Start(new ProcessStartInfo(ServiceExePath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void StopTemporary()
        {
            try
            {
                if (_temporary != null && !_temporary.HasExited)
                    _temporary.Kill();
            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                _temporary = null;
            }
        }

        /// <summary>Restarts the in-session (user mode) engine.</summary>
        public static bool RestartTemporary()
        {
            StopTemporary();
            return StartTemporary();
        }

        /// <summary>Stops the temporary engine and runs the elevated install script.</summary>
        public static bool InstallAsService()
        {
            StopTemporary();

            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "install-service.cmd");
            if (!File.Exists(script))
                return false;

            return RunElevated(script, null);
        }

        public static bool StartInstalledElevated()
        {
            return RunElevated("sc.exe", "start " + ServiceName);
        }

        /// <summary>Stops then starts the installed service (elevated).</summary>
        public static bool RestartInstalled()
        {
            return RunElevated("cmd.exe",
                "/c sc stop " + ServiceName + " >nul & ping -n 4 127.0.0.1 >nul & sc start " + ServiceName);
        }

        /// <summary>Runs the elevated uninstall script (stops and deletes the service).</summary>
        public static bool UninstallService()
        {
            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uninstall-service.cmd");
            if (!File.Exists(script))
                return false;

            return RunElevated(script, null);
        }

        private static bool RunHidden(string fileName, string arguments, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var info = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(info))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(5000))
                        return false;

                    exitCode = process.ExitCode;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Run(string fileName, string arguments)
        {
            int exitCode;
            return RunHidden(fileName, arguments, out exitCode) && exitCode == 0;
        }

        private static bool RunElevated(string fileName, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(fileName) { UseShellExecute = true, Verb = "runas" };
                if (!string.IsNullOrEmpty(arguments))
                    info.Arguments = arguments;

                Process.Start(info);
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
