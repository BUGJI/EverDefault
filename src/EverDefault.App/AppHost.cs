using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.App
{
    /// <summary>Shared services the main window exposes to each page.</summary>
    public interface IAppHost
    {
        bool ServiceRunning { get; }

        Task<IpcResponse> SendAsync(string command, object payload = null);

        void Refresh();

        /// <summary>Switches the main window to a page key (home / protection / activity / settings).</summary>
        void Navigate(string pageKey);

        /// <summary>Opens the activity page with the given filter (all / blocked / failed).</summary>
        void ShowActivity(string filter);

        void ExportBackup();

        void ImportBackup();

        void ShowInfo(string message);

        void ShowWarning(string message);

        bool Confirm(string message);

        /// <summary>Applies UI-side settings (theme, startup entry, update check).</summary>
        void ApplyUserSettings(AppSettings settings);
    }

    /// <summary>A page that reacts to the periodic status/rules/log snapshot.</summary>
    public interface IAppPage
    {
        void ApplySnapshot(RefreshSnapshot snapshot);
    }

    public sealed class RefreshSnapshot
    {
        public bool ServiceRunning;

        /// <summary>True while the app is still waiting for a just-launched engine.</summary>
        public bool ServiceStarting;

        public string StatusError;

        public ServiceStatus Status;

        public List<RuleRow> Rules;

        public List<string> Conflicts;

        public List<Guid> ConflictingRuleIds;

        public List<ActivityItem> Logs;
    }
}
