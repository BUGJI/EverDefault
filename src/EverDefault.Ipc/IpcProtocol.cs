using System.Collections.Generic;
using EverDefault.Core.Model;

namespace EverDefault.Ipc
{
    /// <summary>Pipe name and command names shared by the service and the tray.</summary>
    public static class IpcProtocol
    {
        public const string PipeName = "EverDefault.Service.v1";

        public const string CommandPing = "ping";
        public const string CommandGetStatus = "getStatus";
        public const string CommandGetRules = "getRules";
        public const string CommandSaveRule = "saveRule";
        public const string CommandSaveRules = "saveRules";
        public const string CommandDeleteRule = "deleteRule";
        public const string CommandDeleteRules = "deleteRules";
        public const string CommandGetLog = "getLog";
        public const string CommandClearLog = "clearLog";
        public const string CommandGetConflicts = "getConflicts";
        public const string CommandGetSettings = "getSettings";
        public const string CommandSaveSettings = "saveSettings";
        public const string CommandSetMonitoring = "setMonitoring";
        public const string CommandReloadRules = "reloadRules";
        public const string CommandPauseAll = "pauseAll";
        public const string CommandResumeAll = "resumeAll";
        public const string CommandScanNamespaces = "scanNamespaces";
        public const string CommandRunAll = "runAll";
        public const string CommandRunRule = "runRule";
        public const string CommandRunRules = "runRules";
        public const string CommandSaveRuleAndRun = "saveRuleAndRun";
        public const string CommandSaveRulesAndRun = "saveRulesAndRun";
    }

    public sealed class IpcRequest
    {
        public string Command { get; set; }

        public string Payload { get; set; }
    }

    public sealed class IpcResponse
    {
        public bool Success { get; set; }

        public string Error { get; set; }

        public string Payload { get; set; }

        public static IpcResponse Ok(string payload = null)
        {
            return new IpcResponse { Success = true, Payload = payload };
        }

        public static IpcResponse Fail(string error)
        {
            return new IpcResponse { Success = false, Error = error };
        }
    }

    public sealed class ServiceStatus
    {
        public bool Running { get; set; }

        public bool MonitoringEnabled { get; set; }

        /// <summary>Whether the connected engine runs as a temporary console or an installed service.</summary>
        public ServiceHostMode HostMode { get; set; }

        public string OsDescription { get; set; }

        public string Version { get; set; }

        public System.DateTime StartedUtc { get; set; }

        public int ActiveWatchers { get; set; }

        public int RuleCount { get; set; }

        /// <summary>Log entries recorded while monitoring was active since this engine started.</summary>
        public int ObservedCount { get; set; }

        /// <summary>Changes successfully restored/deleted since this engine started.</summary>
        public int BlockedCount { get; set; }

        /// <summary>Restore/delete attempts that failed since this engine started.</summary>
        public int FailedCount { get; set; }

        /// <summary>When set and in the future, monitoring is temporarily paused.</summary>
        public System.DateTime? PauseUntilUtc { get; set; }

        /// <summary>Increments whenever rules are reloaded; clients refetch only when it changes.</summary>
        public int RulesRevision { get; set; }

        /// <summary>Id of the newest log entry; clients use it to fetch only new entries.</summary>
        public long LogLastId { get; set; }
    }

    /// <summary>One "This PC" / Desktop namespace item discovered by the service.</summary>
    public sealed class NameSpaceScanEntry
    {
        public string Guid { get; set; }

        public string DisplayName { get; set; }

        public string TargetPath { get; set; }

        public string NameSpaceKeyPath { get; set; }

        public string ClsidKeyPath { get; set; }

        public string Location { get; set; }
    }

    public sealed class NameSpaceScanResult
    {
        public List<NameSpaceScanEntry> Entries { get; set; } = new List<NameSpaceScanEntry>();
    }

    /// <summary>How the engine that answered the request is being hosted.</summary>
    public enum ServiceHostMode
    {
        User = 0,
        Service = 1
    }

    public sealed class LogQuery
    {
        public int Max { get; set; } = 500;

        public System.Guid? RuleId { get; set; }

        /// <summary>When set, returns only entries newer than this Id, oldest first.</summary>
        public long? AfterId { get; set; }
    }

    public sealed class LogPage
    {
        public List<ChangeLogEntry> Entries { get; set; } = new List<ChangeLogEntry>();

        /// <summary>Last Id currently stored in the engine, so clients can detect resets.</summary>
        public long LastId { get; set; }
    }

    /// <summary>Result of a manual "check and fix now" request.</summary>
    public sealed class RunNowResult
    {
        /// <summary>True when the run actually executed on the engine.</summary>
        public bool Started { get; set; }

        /// <summary>Number of rules attempted in this run.</summary>
        public int Queued { get; set; }

        /// <summary>Number of enabled rules that were not run (conflict, OS, disabled, etc.).</summary>
        public int Skipped { get; set; }

        /// <summary>Number of attempted rules that finished without an exception.</summary>
        public int Succeeded { get; set; }

        /// <summary>Number of attempted rules that raised an exception.</summary>
        public int Failed { get; set; }

        public string Message { get; set; }
    }
}
