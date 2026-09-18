namespace EverDefault.Core.Model
{
    public sealed class AppSettings
    {
        public bool MonitoringEnabled { get; set; } = true;

        public bool RunAtStartup { get; set; } = true;

        /// <summary>Manual override for OS-specific behaviour. Unknown = auto-detect.</summary>
        public OsFamily OsOverride { get; set; } = OsFamily.Unknown;

        public int LogRetentionDays { get; set; } = 30;

        public bool RemindAntivirusWhitelist { get; set; } = true;

        /// <summary>Service context: apply per-user rules to all loaded profiles or just one.</summary>
        public UserScopeMode TargetUserScope { get; set; } = UserScopeMode.AllUsers;
    }
}
