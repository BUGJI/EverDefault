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

    public sealed class ScrollState
    {
        public double Vertical;

        public double Horizontal;
    }

    public sealed class RefreshSnapshot
    {
        public bool ServiceRunning;

        public string StatusError;

        public ServiceStatus Status;

        public List<RuleRow> Rules;

        public List<string> Conflicts;

        public List<LogRow> Logs;
    }
}
