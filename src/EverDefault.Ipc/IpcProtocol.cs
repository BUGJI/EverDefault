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
        public const string CommandDeleteRule = "deleteRule";
        public const string CommandGetLog = "getLog";
        public const string CommandGetConflicts = "getConflicts";
        public const string CommandGetSettings = "getSettings";
        public const string CommandSaveSettings = "saveSettings";
        public const string CommandSetMonitoring = "setMonitoring";
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

        public string OsDescription { get; set; }

        public string Version { get; set; }

        public System.DateTime StartedUtc { get; set; }

        public int ActiveWatchers { get; set; }

        public int RuleCount { get; set; }
    }

    public sealed class LogQuery
    {
        public int Max { get; set; } = 500;

        public System.Guid? RuleId { get; set; }
    }

    public sealed class LogPage
    {
        public List<ChangeLogEntry> Entries { get; set; } = new List<ChangeLogEntry>();
    }
}
