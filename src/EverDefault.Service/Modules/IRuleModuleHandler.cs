using System.Collections.Generic;
using EverDefault.Core.Engine;
using EverDefault.Core.Model;

namespace EverDefault.Service.Modules
{
    /// <summary>
    /// Per-module logic: where to watch, how to capture a baseline, and what to do when
    /// a change is detected. Handlers are invoked on the watcher/scheduler threads.
    /// </summary>
    public interface IRuleModuleHandler
    {
        RuleModule Module { get; }

        IEnumerable<WatchRegistration> GetWatchTargets(RuleBase rule);

        void CaptureBaseline(RuleBase rule);

        void Handle(RuleBase rule, ChangeOrigin origin);
    }
}
