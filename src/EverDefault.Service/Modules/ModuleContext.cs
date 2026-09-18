using System;
using EverDefault.Core.Model;
using EverDefault.Core.Os;
using EverDefault.Core.Storage;
using EverDefault.Registry.Access;
using EverDefault.Registry.Users;

namespace EverDefault.Service.Modules
{
    /// <summary>Shared dependencies handed to every module handler.</summary>
    public sealed class ModuleContext
    {
        public IRegistryAccess Registry { get; set; }

        public IBaselineStore Baselines { get; set; }

        public IChangeLogStore Log { get; set; }

        public Func<AppSettings> GetSettings { get; set; }

        public OsInfo Os { get; set; }

        public UserScope UserScope { get; set; }

        public Action<string> Trace { get; set; }

        public AppSettings Settings
        {
            get { return GetSettings != null ? GetSettings() : new AppSettings(); }
        }

        public void WriteLog(
            RuleBase rule,
            string keyPath,
            string valueName,
            string oldData,
            string newData,
            string action,
            string result,
            ChangeOrigin origin,
            string message)
        {
            Log.Append(new ChangeLogEntry
            {
                RuleId = rule != null ? rule.Id : (Guid?)null,
                Module = rule != null ? rule.Module : (RuleModule?)null,
                KeyPath = keyPath,
                ValueName = valueName,
                OldData = oldData,
                NewData = newData,
                Action = action,
                Result = result,
                Origin = origin,
                Message = message
            });
        }

        public void WriteTrace(string message)
        {
            if (Trace != null)
                Trace(message);
        }
    }
}
