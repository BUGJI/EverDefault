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
using EverDefault.Registry.NameSpace;
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
        private const int MaxLogEntries = 5000;

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

        private HashSet<Guid> _conflicting = new HashSet<Guid>();
        private List<ConflictReport> _conflictReports = new List<ConflictReport>();
        private int _runInProgress;
        private Timer _scheduler;
        private Timer _userWatch;
        private Timer _logMaintenance;
        private PipeServer _pipe;
        private List<RuleBase> _ruleCache = new List<RuleBase>();
        private List<string> _scopeSids = new List<string>();
        private DateTime _startedUtc;
        private int _rulesRevision = 1;
        private long _logLastId;
        private int _observedCount;
        private int _blockedCount;
        private int _failedCount;
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
            log.Appended += OnLogAppended;

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

        private void OnLogAppended(ChangeLogEntry entry)
        {
            if (entry == null)
                return;

            var result = entry.Result ?? string.Empty;
            if (string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _failedCount);
            else if (string.Equals(result, "logged", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _observedCount);
            else if (string.Equals(result, "restored", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(result, "deleted", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(result, "removed", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _blockedCount);

            // Events may be raised out of order across handler threads; keep the maximum.
            while (true)
            {
                var current = Interlocked.Read(ref _logLastId);
                if (entry.Id <= current)
                    break;
                if (Interlocked.CompareExchange(ref _logLastId, entry.Id, current) == current)
                    break;
            }
        }

        public void Start()
        {
            _startedUtc = DateTime.UtcNow;
            UpgradeLegacyDefaultAppRules();
            Reload();

            MaintainLog(_settings.Load());
            Interlocked.Exchange(ref _logLastId, _log.LastId);
            _logMaintenance = new Timer(OnLogMaintenanceTick, null, 60000, 3600000);

            _pipe = new PipeServer(IpcProtocol.PipeName, new IpcDispatcher(this).Handle);
            _pipe.Log += RaiseTrace;
            _pipe.Start();

            // Only an installed service starts before a user logs on, so only it needs the
            // fallback re-check. Console (user) mode already runs inside a loaded profile.
            if (HostMode == ServiceHostMode.Service)
                _userWatch = new Timer(OnUserWatchTick, null, 60000, 60000);

            RaiseTrace("engine started on " + _os);
        }

        private void MaintainLog(AppSettings settings)
        {
            try
            {
                var days = settings == null ? 30 : settings.LogRetentionDays;
                if (days <= 0)
                    days = 30;

                _log.Maintenance(DateTime.UtcNow.AddDays(-days), MaxLogEntries);
            }
            catch (Exception ex)
            {
                RaiseTrace("log maintenance: " + ex.Message);
            }
        }

        private void OnLogMaintenanceTick(object state)
        {
            MaintainLog(_settings.Load());
        }

        /// <summary>
        /// One-time upgrade: merges legacy per-extension default-app rules (one rule per
        /// ProgId) into grouped rules that carry a per-extension ProgId map.
        /// </summary>
        private void UpgradeLegacyDefaultAppRules()
        {
            try
            {
                var rules = _rules.GetAll().ToList();
                if (DefaultAppRuleGrouper.Upgrade(rules))
                {
                    _rules.ReplaceAll(rules);
                    RaiseTrace("merged legacy default-app rules; " + rules.Count + " rule(s) remain");
                }
            }
            catch (Exception ex)
            {
                RaiseTrace("default-app rule upgrade failed: " + ex.Message);
            }
        }

        public void Reload()
        {
            lock (_sync)
            {
                Interlocked.Increment(ref _rulesRevision);
                StopWatchers();
                _nextDue.Clear();

                var settings = _settings.Load();
                _context.UserScope = new UserScope(settings.TargetUserScope);
                _scopeSids = _context.UserScope.Sids
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var rules = _rules.GetAll();
                _ruleCache = rules.ToList();

                var reports = _detector.Detect(rules).ToList();
                _conflictReports = reports;
                _conflicting = new HashSet<Guid>();
                foreach (var report in reports)
                {
                    if (report.First != null) _conflicting.Add(report.First.Id);
                    if (report.Second != null) _conflicting.Add(report.Second.Id);
                }

                var hasScheduled = false;

                foreach (var rule in rules.Where(r => r.Enabled && !_conflicting.Contains(r.Id)))
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

        /// <summary>Re-checks the loaded user hives and reloads when the resolved scope changes.</summary>
        private void OnUserWatchTick(object state)
        {
            try
            {
                if (_settings.Load().TargetUserScope != UserScopeMode.AllUsers)
                    return;

                var current = new UserScope(UserScopeMode.AllUsers).Sids
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                bool changed;
                lock (_sync)
                {
                    changed = !_scopeSids.SequenceEqual(current, StringComparer.OrdinalIgnoreCase);
                }

                if (!changed)
                    return;

                RaiseTrace("user scope changed to " + current.Count + " user(s); reloading");
                Reload();
            }
            catch (Exception ex)
            {
                RaiseTrace("user watch: " + ex.Message);
            }
        }

        /// <summary>Called on session logon/unlock: rebuild the per-user scope once the hive loads.</summary>
        public void OnUserSessionChanged()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(2500);
                try
                {
                    Reload();
                }
                catch (Exception ex)
                {
                    RaiseTrace("session reload: " + ex.Message);
                }
            });
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

        private bool RunRule(
            RuleBase rule, IRuleModuleHandler handler, ChangeOrigin origin, bool honorPause = true)
        {
            var settings = _settings.Load();
            if (!settings.MonitoringEnabled)
                return false;

            var now = DateTime.UtcNow;
            if (honorPause)
            {
                if (settings.PauseUntilUtc.HasValue && settings.PauseUntilUtc.Value > now)
                    return false;

                if (rule != null && rule.PausedUntilUtc.HasValue && rule.PausedUntilUtc.Value > now)
                    return false;
            }

            try
            {
                handler.Handle(rule, origin);
                return true;
            }
            catch (Exception ex)
            {
                RaiseTrace("rule '" + rule.Name + "' failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Runs every enabled, non-conflicting rule once. Explicit manual runs skip the
        /// temporary pause, but still respect the global monitoring switch.
        /// </summary>
        public RunNowResult RunAllNow()
        {
            var settings = _settings.Load();
            if (!settings.MonitoringEnabled)
            {
                return new RunNowResult
                {
                    Message = "保护已关闭，请先在设置中开启保护，再执行。"
                };
            }

            List<RuleBase> eligible;
            int total;
            lock (_sync)
            {
                total = _ruleCache.Count;
                eligible = _ruleCache.Where(r => IsRunnable(r, settings)).ToList();
            }

            if (eligible.Count == 0)
            {
                return new RunNowResult
                {
                    Skipped = total,
                    Message = "没有可执行的保护规则。"
                };
            }

            return ExecuteManualRun(eligible, total);
        }

        /// <summary>Runs a single rule once, even when its mode is Manual.</summary>
        public RunNowResult RunRuleNow(Guid id)
        {
            var settings = _settings.Load();
            if (!settings.MonitoringEnabled)
            {
                return new RunNowResult
                {
                    Message = "保护已关闭，请先在设置中开启保护，再执行。"
                };
            }

            RuleBase rule;
            bool conflicting;
            lock (_sync)
            {
                rule = _ruleCache.FirstOrDefault(r => r.Id == id);
                conflicting = _conflicting.Contains(id);
            }

            if (rule == null)
                return new RunNowResult { Message = "规则不存在，可能已被删除。" };

            if (conflicting)
            {
                return new RunNowResult
                {
                    Message = "这条规则与其他规则冲突，暂时不会执行。请先处理冲突。"
                };
            }

            if (!IsRunnable(rule, settings))
            {
                return new RunNowResult
                {
                    Message = rule.Enabled
                        ? "当前系统不支持这条规则。"
                        : "这条规则已停用，启用后再执行。"
                };
            }

            return ExecuteManualRun(new List<RuleBase> { rule }, 1);
        }

        /// <summary>
        /// Saves the given rules and then applies them immediately. Use this for edits and
        /// newly added protections; pause/enable toggles should keep using the plain save.
        /// </summary>
        public RunNowResult SaveRuleAndRun(RuleBase rule)
        {
            return SaveRulesAndRun(new[] { rule });
        }

        public RunNowResult SaveRulesAndRun(IEnumerable<RuleBase> rules)
        {
            var list = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => r != null)
                .ToList();
            if (list.Count == 0)
                return new RunNowResult { Message = "没有可保存的规则。" };

            SaveRules(list);

            var settings = _settings.Load();
            if (!settings.MonitoringEnabled)
            {
                return new RunNowResult
                {
                    Skipped = list.Count,
                    Message = "已保存。保护已关闭，未执行；开启保护后再应用。"
                };
            }

            List<RuleBase> eligible;
            lock (_sync)
            {
                eligible = list.Where(r => IsRunnable(r, settings)).ToList();
            }

            if (eligible.Count == 0)
            {
                return new RunNowResult
                {
                    Skipped = list.Count,
                    Message = "已保存，但没有可执行的规则（停用/冲突/不适用）。"
                };
            }

            var result = ExecuteManualRun(eligible, list.Count);
            result.Message = "已保存。" + result.Message;
            return result;
        }

        /// <summary>Runs several rules by id once, even when they are in Manual mode.</summary>
        public RunNowResult RunRulesNow(IEnumerable<Guid> ids)
        {
            var settings = _settings.Load();
            if (!settings.MonitoringEnabled)
            {
                return new RunNowResult
                {
                    Message = "保护已关闭，请先在设置中开启保护，再执行。"
                };
            }

            var wanted = (ids ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (wanted.Count == 0)
                return new RunNowResult { Message = "没有要执行的规则。" };

            List<RuleBase> eligible;
            lock (_sync)
            {
                eligible = wanted
                    .Select(id => _ruleCache.FirstOrDefault(r => r.Id == id))
                    .Where(r => r != null && IsRunnable(r, settings))
                    .ToList();
            }

            if (eligible.Count == 0)
            {
                return new RunNowResult
                {
                    Skipped = wanted.Count,
                    Message = "没有可执行的规则（不存在/停用/冲突/不适用）。"
                };
            }

            return ExecuteManualRun(eligible, wanted.Count);
        }

        /// <summary>Executes the rules synchronously and returns real success/failure counts.</summary>
        private RunNowResult ExecuteManualRun(List<RuleBase> rules, int total)
        {
            if (Interlocked.CompareExchange(ref _runInProgress, 1, 0) != 0)
            {
                return new RunNowResult
                {
                    Message = "上一次手动执行还没结束，请稍候再试。"
                };
            }

            var succeeded = 0;
            var failed = 0;

            try
            {
                foreach (var rule in rules)
                {
                    IRuleModuleHandler handler;
                    if (!_handlers.TryGetValue(rule.Module, out handler))
                    {
                        failed++;
                        continue;
                    }

                    if (RunRule(rule, handler, ChangeOrigin.Manual, honorPause: false))
                        succeeded++;
                    else
                        failed++;
                }

                RaiseTrace("manual run finished: " + rules.Count + " rule(s)");
            }
            catch (Exception ex)
            {
                RaiseTrace("manual run: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _runInProgress, 0);
            }

            var skipped = Math.Max(0, total - rules.Count);
            var message = "已执行 " + (succeeded + failed) + " 条规则：完成 " + succeeded + " 条";
            if (failed > 0)
                message += "，出错 " + failed + " 条";
            if (skipped > 0)
                message += "，跳过 " + skipped + " 条（停用/冲突/不适用）";
            message += "。";

            return new RunNowResult
            {
                Started = true,
                Queued = rules.Count,
                Skipped = skipped,
                Succeeded = succeeded,
                Failed = failed,
                Message = message
            };
        }

        private bool IsRunnable(RuleBase rule, AppSettings settings)
        {
            if (rule == null || !rule.Enabled)
                return false;

            if (_conflicting.Contains(rule.Id))
                return false;

            if (!_handlers.ContainsKey(rule.Module))
                return false;

            var family = settings.OsOverride != OsFamily.Unknown ? settings.OsOverride : _os.Family;
            return rule.Supports(family);
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
            var settings = _settings.Load();

            return new ServiceStatus
            {
                Running = true,
                MonitoringEnabled = settings.MonitoringEnabled,
                PauseUntilUtc = settings.PauseUntilUtc,
                HostMode = HostMode,
                OsDescription = _os.ToString(),
                Version = typeof(EngineHost).Assembly.GetName().Version.ToString(),
                StartedUtc = _startedUtc,
                ActiveWatchers = _watchers.Count,
                RuleCount = rules.Count,
                ObservedCount = Volatile.Read(ref _observedCount),
                BlockedCount = Volatile.Read(ref _blockedCount),
                FailedCount = Volatile.Read(ref _failedCount),
                RulesRevision = Volatile.Read(ref _rulesRevision),
                LogLastId = Interlocked.Read(ref _logLastId)
            };
        }

        public RuleListResult GetRules()
        {
            List<RuleBase> rules;
            List<ConflictReport> reports;
            HashSet<Guid> conflicting;
            lock (_sync)
            {
                rules = _ruleCache;
                reports = _conflictReports;
                conflicting = _conflicting;
            }

            return new RuleListResult
            {
                Rules = rules.ToList(),
                Conflicts = reports.Select(c => c.ToString()).ToList(),
                ConflictingRuleIds = conflicting.ToList()
            };
        }

        public void SaveRule(RuleBase rule)
        {
            SaveRules(new[] { rule });
        }

        public void SaveRules(IEnumerable<RuleBase> rules)
        {
            var list = (rules ?? Enumerable.Empty<RuleBase>())
                .Where(r => r != null)
                .ToList();
            if (list.Count == 0)
                return;

            _rules.UpsertMany(list);

            _baselines.BeginBatch();
            try
            {
                foreach (var rule in list)
                {
                    IRuleModuleHandler handler;
                    if (_handlers.TryGetValue(rule.Module, out handler))
                        handler.CaptureBaseline(rule);
                }
            }
            finally
            {
                _baselines.EndBatch();
            }

            Reload();
        }

        public void DeleteRule(Guid id)
        {
            DeleteRules(new[] { id });
        }

        public void DeleteRules(IEnumerable<Guid> ids)
        {
            var list = (ids ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (list.Count == 0)
                return;

            _rules.DeleteMany(list);
            _baselines.DeleteForRules(list);
            Reload();
        }

        public IReadOnlyList<ChangeLogEntry> GetLog(int max, Guid? ruleId, long? afterId = null)
        {
            return afterId.HasValue
                ? _log.QueryAfter(afterId.Value, max)
                : _log.Query(max, ruleId);
        }

        public long GetLogLastId()
        {
            return _log.LastId;
        }

        public void ClearLog()
        {
            _log.Clear();
            Interlocked.Exchange(ref _observedCount, 0);
            Interlocked.Exchange(ref _blockedCount, 0);
            Interlocked.Exchange(ref _failedCount, 0);
            Interlocked.Exchange(ref _logLastId, 0);
        }

        public AppSettings GetSettings()
        {
            return _settings.Load();
        }

        public void SaveSettings(AppSettings settings)
        {
            _settings.Save(settings);
            MaintainLog(settings);
            Reload();
        }

        public void SetMonitoring(bool enabled)
        {
            var settings = _settings.Load();
            settings.MonitoringEnabled = enabled;
            _settings.Save(settings);
            Reload();
        }

        public void PauseAll(int minutes)
        {
            var settings = _settings.Load();
            settings.PauseUntilUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, minutes));
            _settings.Save(settings);
        }

        public void ResumeAll()
        {
            var settings = _settings.Load();
            settings.PauseUntilUtc = null;
            _settings.Save(settings);
        }

        /// <summary>Lists "This PC" / Desktop namespace entries for the configured user scope.</summary>
        public List<NameSpaceScanEntry> ScanNameSpaces()
        {
            var roots = new List<string>();
            var scope = _context.UserScope;

            foreach (var root in NameSpaceScanner.DefaultRoots)
            {
                if (scope == null)
                {
                    roots.Add(root);
                    continue;
                }

                foreach (var expanded in scope.ExpandCurrentUser(root))
                {
                    if (!roots.Contains(expanded, StringComparer.OrdinalIgnoreCase))
                        roots.Add(expanded);
                }
            }

            var found = NameSpaceScanner.Scan(_registry, roots);
            return found.Select(entry => new NameSpaceScanEntry
            {
                Guid = entry.Guid,
                DisplayName = entry.DisplayName,
                TargetPath = entry.TargetPath,
                NameSpaceKeyPath = entry.NameSpaceKeyPath,
                ClsidKeyPath = entry.ClsidKeyPath,
                Location = DescribeLocation(entry.NameSpaceKeyPath)
            }).ToList();
        }

        private static string DescribeLocation(string keyPath)
        {
            var text = keyPath ?? string.Empty;
            var where = text.IndexOf(@"\Desktop\NameSpace", StringComparison.OrdinalIgnoreCase) >= 0
                ? "桌面"
                : "此电脑";

            if (text.StartsWith(@"HKEY_USERS\", StringComparison.OrdinalIgnoreCase))
                return where + " · 当前用户";
            if (text.StartsWith(@"HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
                return where + " · 当前用户";

            return where + " · 所有用户";
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

            if (_userWatch != null)
            {
                _userWatch.Dispose();
                _userWatch = null;
            }

            if (_logMaintenance != null)
            {
                _logMaintenance.Dispose();
                _logMaintenance = null;
            }

            _log.Appended -= OnLogAppended;
            _log.Flush();

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
