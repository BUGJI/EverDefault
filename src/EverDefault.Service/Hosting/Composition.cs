using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Registry.Access;
using EverDefault.Service.Modules;

namespace EverDefault.Service.Hosting
{
    /// <summary>
    /// Composition root for the service. Kept separate so the same dependencies can be
    /// reused by tests or a future in-process host.
    /// </summary>
    public static class Composition
    {
        public static EngineHost Build(
            IRuleStore ruleStore,
            IBaselineStore baselineStore,
            IChangeLogStore logStore,
            ISettingsStore settingsStore,
            IRegistryAccess registry,
            Core.Os.OsInfo os)
        {
            return new EngineHost(ruleStore, baselineStore, logStore, settingsStore, registry, os);
        }
    }
}
