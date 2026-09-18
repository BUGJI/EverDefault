using System;
using System.IO;

namespace EverDefault.Persistence
{
    /// <summary>
    /// Shared data location. ProgramData so the service (SYSTEM) and the per-user tray
    /// read/write the same files.
    /// </summary>
    public static class DataPaths
    {
        public static string Root
        {
            get
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "EverDefault");
                return root;
            }
        }

        public static string RulesFile
        {
            get { return Path.Combine(Root, "rules.json"); }
        }

        public static string BaselinesFile
        {
            get { return Path.Combine(Root, "baselines.json"); }
        }

        public static string ChangeLogFile
        {
            get { return Path.Combine(Root, "changelog.json"); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(Root, "settings.json"); }
        }
    }
}
