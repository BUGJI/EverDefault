using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EverDefault.Core.Engine;
using EverDefault.Core.Model;
using EverDefault.Core.Os;
using EverDefault.Core.Storage;
using EverDefault.Ipc;
using EverDefault.Registry.Access;
using EverDefault.Registry.Users;
using EverDefault.Registry.Watching;
using EverDefault.Service.Modules;
using Win32 = Microsoft.Win32;

namespace EverDefault.Service.Hosting
{
    /// <summary>
    /// Owns the watchers/scheduler, reloads rules, and serves IPC requests.
    /// Thread-safety: rule reloads are serialized; handlers run on worker threads.
    /// </summary>
    public sealed class EngineHost : IDisposable
    {
        private readonly IRuleStore _rules;
        private readonly IBaselineStore _baselines;
        private readonly IChangeLogStore _log;
        private readonly ISettingsStore _settings;
        private readonly IRegistryAccess _registry;
        private readonly OsInfo _os;
        private readonly RuleConflictDetector _detector = new RuleConflictDetector();
        private readonly Dictionary<RuleModule, IRuleModuleHandler> _handlers;
        private readonly List<WatcherRegistration> _watchers = new List<WatcherRegistration>();
        private readonly Dictionary<Guid, DateTime> _nextDue = new Dictionary<Guid, DateTime>();
        private readonly object _sync = new object();
        private readonly ModuleContext _context;

        private Timer _scheduler;
        private PipeServer _pipe;
        private List<RuleBase> _ruleCache = new List<RuleBase>();
        private DateTime _startedUtc;
        private bool _disposed;

        public event Action<string> Trace;

        /// <summary>Set by the program entry point: temporary console (User) or installed service.</summary>
        public ServiceHostMode HostMode { get; set; } = ServiceHostMode.User;

        public EngineHost(
            IRuleStore rules,
            IBaselineStore baselines,
            IChangeLogStore log,
            ISettingsStore settings,
            IRegistryAccess registry,
            OsInfo os)
        {
            _rules = rules;
            _baselines = baselines;
            _log = log;
            _settings = settings;
            _registry = registry;
            _os = os;

            var context = new ModuleContext
            {
                Registry = registry,
                Baselines = baselines,
                Log = log,
                GetSettings = settings.Load,
                Os = os,
                UserScope = new UserScope(settings.Load().TargetUserScope),
                Trace = message => RaiseTrace(message)
            };
            _context = context;

            _handlers = new Dictionary<RuleModule, IRuleModuleHandler>
            {
                { RuleModule.CustomRegistry, new CustomRegistryHandler(context) },
                { RuleModule.NameSpace, new NameSpaceHandler(context) },
                { RuleModule.DefaultApp, new DefaultAppHandler(context) }
            };
        }

        public void Start()
        {
            _startedUtc = DateTime.UtcNow;
            Reload();

            _pipe = new PipeServer(IpcProtocol.PipeName, new IpcDispatcher(this).Handle);
            _pipe.Log += RaiseTrace;
            _pipe.Start();
            RaiseTrace("engine started on " + _os);
        }

        public void Reload()
        {
            lock (_sync)
            {
                StopWatchers();
                _nextDue.Clear();

                var settings = _settings.Load();
                _context.UserScope = new UserScope(settings.TargetUserScope);
                var rules = _rules.GetAll();
                _ruleCache = rules.ToList();

                var conflicting = _detector.ConflictingRuleIds(rules);
                var hasScheduled = false;

                foreach (var rule in rules.Where(r => r.Enabled && !conflicting.Contains(r.Id)))
                {
                    var family = settings.OsOverride != OsFamily.Unknown ? settings.OsOverride : _os.Family;
                    if (!rule.Supports(family))
                        continue;

                    IRuleModuleHandler handler;
                    if (!_handlers.TryGetValue(rule.Module, out handler))
                        continue;

                    if (rule.Mode == RuleMode.Schedule)
                    {
                        hasScheduled = true;
                        _nextDue[rule.Id] = DateTime.UtcNow.AddSeconds(Math.Max(5, rule.IntervalSeconds));
                        continue;
                    }

                    if (rule.Mode == RuleMode.Manual)
                        continue;

                    foreach (var target in handler.GetWatchTargets(rule))
                        StartWatcher(rule, handler, target);
                }

                ConfigureScheduler(hasScheduled);
            }
        }

        public void StopMonitoring()
        {
            lock (_sync)
            {
                StopWatchers();
                ConfigureScheduler(false);
            }
        }

        private void ConfigureScheduler(bool enabled)
        {
            if (enabled)
            {
                if (_scheduler == null)
                    _scheduler = new Timer(OnScheduleTick, null, 1000, 1000);
            }
            else if (_scheduler != null)
            {
                _scheduler.Dispose();
                _scheduler = null;
            }
        }

        private void StartWatcher(RuleBase rule, IRuleModuleHandler handler, WatchRegistration target)
        {
            RegPath path;
            try
            {
                path = RegPath.Parse(target.KeyPath);
            }
            catch (Exception ex)
            {
                RaiseTrace("invalid watch path '" + target.KeyPath + "': " + ex.Message);
                return;
            }

            var subKey = path.SubKey;
            RegistryWatcher watcher = null;

            // If the exact key does not exist yet, walk up to the nearest existing ancestor so
            // that creating the key later still triggers the rule.
            while (watcher == null)
            {
                using (var hive = OpenHive(path.Hive))
                {
                    if (hive == null)
                        return;

                    try
                    {
                        watcher = new RegistryWatcher(hive, subKey);
                    }
                    catch (Exception)
                    {
                        watcher = null;
                    }
                }

                if (watcher != null)
                    break;

                var slash = subKey.LastIndexOf('\\');
                if (slash < 0)
                {
                    RaiseTrace("cannot watch '" + target.KeyPath + "': no existing ancestor");
                    return;
                }

                subKey = subKey.Substring(0, slash);
            }

            if (!string.Equals(subKey, path.SubKey, StringComparison.OrdinalIgnoreCase))
                RaiseTrace("watching closest existing ancestor: " + subKey + " (for " + target.KeyPath + ")");

            var registration = new WatcherRegistration
            {
                Rule = rule,
                Handler = handler,
                Target = target,
                Watcher = watcher
            };

            watcher.Changed += (sender, args) => OnWatcherChanged(registration);
            watcher.Start();
            _watchers.Add(registration);
        }

        private void OnWatcherChanged(WatcherRegistration registration)
        {
            if (Interlocked.CompareExchange(ref registration.Pending, 1, 0) != 0)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(500); // debounce
                Interlocked.Exchange(ref registration.Pending, 0);
                RunRule(registration.Rule, registration.Handler, ChangeOrigin.Watcher);
            });
        }

        private void OnScheduleTick(object state)
        {
            try
            {
                List<Guid> due;
                lock (_sync)
                {
                    var now = DateTime.UtcNow;
                    due = _nextDue.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList();
                    foreach (var id in due)
                    {
                        var rule = _ruleCache.FirstOrDefault(r => r.Id == id);
                        if (rule != null)
                            _nextDue[id] = now.AddSeconds(Math.Max(5, rule.IntervalSeconds));
                    }
                }

                foreach (var id in due)
                {
                    var rule = _ruleCache.FirstOrDefault(r => r.Id == id);
                    if (rule == null)
                        continue;

                    IRuleModuleHandler handler;
                    if (_handlers.TryGetValue(rule.Module, out handler))
                        RunRule(rule, handler, ChangeOrigin.Scheduler);
                }
            }
            catch (Exception ex)
            {
                RaiseTrace("scheduler: " + ex.Message);
            }
        }

        private void RunRule(RuleBase rule, IRuleModuleHandler handler, ChangeOrigin origin)
        {
            if (_settings.Load().MonitoringEnabled == false)
                return;

            try
            {
                handler.Handle(rule, origin);
            }
            catch (Exception ex)
            {
                RaiseTrace("rule '" + rule.Name + "' failed: " + ex.Message);
            }
        }

        private void StopWatchers()
        {
            foreach (var registration in _watchers)
            {
                try
                {
                    registration.Watcher.Dispose();
                }
                catch
                {
                    // ignored
                }
            }

            _watchers.Clear();
        }

        // ---- operations used by the IPC dispatcher ----

        public ServiceStatus GetStatus()
        {
            var rules = _ruleCache;

            int observed = 0, blocked = 0, failed = 0;
            foreach (var entry in _log.Query(int.MaxValue))
            {
                if (entry.Utc < _startedUtc)
                    continue;

                var result = entry.Result ?? string.Empty;
                if (string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase))
                    failed++;
                else if (string.Equals(result, "logged", StringComparison.OrdinalIgnoreCase))
                    observed++;
                else if (string.Equals(result, "restored", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(result, "deleted", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(result, "removed", StringComparison.OrdinalIgnoreCase))
                    blocked++;
            }

            return new ServiceStatus
            {
                Running = true,
                MonitoringEnabled = _settings.Load().MonitoringEnabled,
                HostMode = HostMode,
                OsDescription = _os.ToString(),
                Version = typeof(EngineHost).Assembly.GetName().Version.ToString(),
                StartedUtc = _startedUtc,
                ActiveWatchers = _watchers.Count,
                RuleCount = rules.Count,
                ObservedCount = observed,
                BlockedCount = blocked,
                FailedCount = failed
            };
        }

        public RuleListResult GetRules()
        {
            var rules = _rules.GetAll();
            var conflicts = _detector.Detect(rules);

            return new RuleListResult
            {
                Rules = rules.ToList(),
                Conflicts = conflicts.Select(c => c.ToString()).ToList(),
                ConflictingRuleIds = conflicts
                    .SelectMany(c => new[] { c.First, c.Second })
                    .Where(r => r != null)
                    .Select(r => r.Id)
                    .Distinct()
                    .ToList()
            };
        }

        public void SaveRule(RuleBase rule)
        {
            _rules.Upsert(rule);

            IRuleModuleHandler handler;
            if (_handlers.TryGetValue(rule.Module, out handler))
                handler.CaptureBaseline(rule);

            Reload();
        }

        public void DeleteRule(Guid id)
        {
            _rules.Delete(id);
            _baselines.DeleteForRule(id);
            Reload();
        }

        public IReadOnlyList<ChangeLogEntry> GetLog(int max, Guid? ruleId)
        {
            return _log.Query(max, ruleId);
        }

        public AppSettings GetSettings()
        {
            return _settings.Load();
        }

        public void SaveSettings(AppSettings settings)
        {
            _settings.Save(settings);
            Reload();
        }

        public void SetMonitoring(bool enabled)
        {
            var settings = _settings.Load();
            settings.MonitoringEnabled = enabled;
            _settings.Save(settings);
            Reload();
        }

        private void RaiseTrace(string message)
        {
            var handler = Trace;
            if (handler != null)
                handler(message);
        }

        private static Win32.RegistryKey OpenHive(RegistryHiveRoot root)
        {
            var view = Win32.RegistryView.Registry64;
            switch (root)
            {
                case RegistryHiveRoot.CurrentUser:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.CurrentUser, view);
                case RegistryHiveRoot.LocalMachine:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.LocalMachine, view);
                case RegistryHiveRoot.Users:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.Users, view);
                case RegistryHiveRoot.ClassesRoot:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.ClassesRoot, view);
                case RegistryHiveRoot.CurrentConfig:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.CurrentConfig, view);
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            lock (_sync)
            {
                StopWatchers();
                ConfigureScheduler(false);
            }

            _pipe?.Dispose();
        }

        private sealed class WatcherRegistration
        {
            public RuleBase Rule;
            public IRuleModuleHandler Handler;
            public WatchRegistration Target;
            public RegistryWatcher Watcher;
            public int Pending;
        }
    }
}
