namespace EverDefault.Core.Model
{
    public sealed class AppSettings
    {
        public bool MonitoringEnabled { get; set; } = true;

        /// <summary>Set once the first-run wizard has been shown or skipped.</summary>
        public bool OnboardingCompleted { get; set; }

        /// <summary>Show a tray balloon when a change is blocked.</summary>
        public bool ShowNotifications { get; set; } = true;

        /// <summary>Show a tray balloon when a block fails.</summary>
        public bool NotifyOnFailure { get; set; } = true;

        /// <summary>Reveal the raw rule tabs and advanced settings.</summary>
        public bool AdvancedMode { get; set; }

        /// <summary>Global temporary pause; the engine resumes automatically after this time.</summary>
        public System.DateTime? PauseUntilUtc { get; set; }

        public bool RunAtStartup { get; set; } = true;

        /// <summary>When launched by the startup entry, begin hidden in the tray.</summary>
        public bool HideToTrayOnStartup { get; set; }

        public bool CheckForUpdates { get; set; } = true;

        public UpdateInterval UpdateCheckInterval { get; set; } = UpdateInterval.Weekly;

        public ThemeMode Theme { get; set; } = ThemeMode.System;

        /// <summary>Manual override for OS-specific behaviour. Unknown = auto-detect.</summary>
        public OsFamily OsOverride { get; set; } = OsFamily.Unknown;

        public int LogRetentionDays { get; set; } = 30;

        /// <summary>Service context: apply per-user rules to all loaded profiles or just one.</summary>
        public UserScopeMode TargetUserScope { get; set; } = UserScopeMode.AllUsers;
    }
}
