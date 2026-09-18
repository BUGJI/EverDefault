using System;
using System.Collections.Generic;
using EverDefault.Core.Engine;
using EverDefault.Core.Model;
using EverDefault.Registry.Access;
using EverDefault.Registry.UserChoice;

namespace EverDefault.Service.Modules
{
    /// <summary>
    /// Enforces the configured ProgId for the selected extensions:
    ///   1. the modern UserChoice key (recomputed hash) - the effective default;
    ///   2. OpenWithProgids (the "Open with" list);
    ///   3. the legacy per-user HKCR\Software\Classes\.ext default.
    /// Works against HKEY_USERS\&lt;SID&gt; so it behaves the same under the service.
    /// </summary>
    public sealed class DefaultAppHandler : IRuleModuleHandler
    {
        private const string FileExtsRelative =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";

        private const string ClassesRelative = @"Software\Classes\";

        private const string HkcuFileExts = @"HKEY_CURRENT_USER\" + FileExtsRelative;

        private const string HkcuClasses = @"HKEY_CURRENT_USER\" + ClassesRelative;

        private readonly ModuleContext _ctx;

        public DefaultAppHandler(ModuleContext ctx)
        {
            _ctx = ctx;
        }

        public RuleModule Module
        {
            get { return RuleModule.DefaultApp; }
        }

        public IEnumerable<WatchRegistration> GetWatchTargets(RuleBase rule)
        {
            var app = rule as DefaultAppRule;
            if (app == null || app.Extensions == null || _ctx.UserScope == null)
                yield break;

            foreach (var extension in app.Extensions)
            {
                if (string.IsNullOrWhiteSpace(extension))
                    continue;

                var ext = UserChoiceManager.NormalizeExtension(extension);
                var fileExts = HkcuFileExts + ext;

                foreach (var expanded in _ctx.UserScope.ExpandCurrentUser(fileExts))
                    yield return new WatchRegistration { KeyPath = expanded, Recursive = true };

                if (app.ManageFileAssociation)
                {
                    foreach (var expanded in _ctx.UserScope.ExpandCurrentUser(HkcuClasses + ext))
                        yield return new WatchRegistration { KeyPath = expanded, Recursive = true };
                }
            }
        }

        public void CaptureBaseline(RuleBase rule)
        {
            // Desired state is rule.ProgId, so no baseline is required.
        }

        public void Handle(RuleBase rule, ChangeOrigin origin)
        {
            var app = rule as DefaultAppRule;
            if (app == null || app.Extensions == null || string.IsNullOrEmpty(app.ProgId))
                return;

            if (_ctx.Settings != null && !_ctx.Settings.MonitoringEnabled)
                return;

            var scope = _ctx.UserScope;
            if (scope == null)
                return;

            foreach (var sid in scope.Sids)
            {
                using (var hive = scope.OpenHive(sid, true))
                {
                    if (hive == null)
                    {
                        _ctx.WriteTrace("user hive not loaded, skipping: " + sid);
                        continue;
                    }

                    var manager = new UserChoiceManager(hive, sid);
                    foreach (var rawExtension in app.Extensions)
                    {
                        var ext = UserChoiceManager.NormalizeExtension(rawExtension);
                        var userPrefix = @"HKEY_USERS\" + sid + @"\";

                        EnforceUserChoice(rule, app, manager, sid, ext,
                            userPrefix + FileExtsRelative + ext, origin);

                        if (app.ManageOpenWith)
                            EnsureProgIdValue(rule, app.ProgId,
                                userPrefix + FileExtsRelative + ext + @"\OpenWithProgids",
                                "打开方式", origin);

                        if (app.ManageFileAssociation)
                        {
                            EnsureProgIdValue(rule, app.ProgId,
                                userPrefix + ClassesRelative + ext + @"\OpenWithProgids",
                                "文件关联(OpenWithProgids)", origin);

                            EnsureDefaultValue(rule, app.ProgId,
                                userPrefix + ClassesRelative + ext,
                                "文件关联(默认值)", origin);
                        }
                    }
                }
            }
        }

        private void EnforceUserChoice(
            RuleBase rule, DefaultAppRule app, UserChoiceManager manager,
            string sid, string extension, string logPath, ChangeOrigin origin)
        {
            UserChoiceSnapshot current;
            try
            {
                current = manager.Capture(extension);
            }
            catch (Exception ex)
            {
                _ctx.WriteLog(rule, logPath, "ProgId", null, app.ProgId, "Restore", "failed", origin,
                    "capture failed: " + ex.Message);
                return;
            }

            if (current.Exists && string.Equals(current.ProgId, app.ProgId, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var now = DateTime.UtcNow;
                var fileTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0,
                    DateTimeKind.Utc).ToFileTimeUtc();

                var hash = UserChoiceHashAlgorithm.Compute(extension, sid, app.ProgId, fileTime);
                var result = manager.WriteRaw(extension, app.ProgId, hash, fileTime);

                _ctx.WriteLog(rule, logPath, "ProgId", current.ProgId, app.ProgId, "Restore",
                    result.Success ? "restored" : "failed", origin,
                    result.Success ? "默认应用已还原" : result.Error);
            }
            catch (Exception ex)
            {
                _ctx.WriteLog(rule, logPath, "ProgId", current.ProgId, app.ProgId, "Restore", "failed",
                    origin, ex.Message);
                _ctx.WriteTrace("default app restore failed: " + ex.Message);
            }
        }

        private void EnsureProgIdValue(RuleBase rule, string progId, string keyPath, string label, ChangeOrigin origin)
        {
            try
            {
                var existing = _ctx.Registry.ReadValue(keyPath, progId);
                if (existing.Exists)
                    return;

                _ctx.Registry.WriteValue(keyPath, progId, "String", string.Empty);
                _ctx.WriteLog(rule, keyPath, progId, null, string.Empty, "Restore", "restored", origin, label + " 已补回");
            }
            catch (Exception ex)
            {
                _ctx.WriteLog(rule, keyPath, progId, null, string.Empty, "Restore", "failed", origin,
                    label + ": " + ex.Message);
            }
        }

        private void EnsureDefaultValue(RuleBase rule, string progId, string keyPath, string label, ChangeOrigin origin)
        {
            try
            {
                var existing = _ctx.Registry.ReadValue(keyPath, string.Empty);
                if (existing.Exists && string.Equals(existing.Data, progId, StringComparison.OrdinalIgnoreCase))
                    return;

                _ctx.Registry.WriteValue(keyPath, string.Empty, "String", progId);
                _ctx.WriteLog(rule, keyPath, "(默认)", existing.Exists ? existing.Data : null, progId,
                    "Restore", "restored", origin, label + " 已写回");
            }
            catch (Exception ex)
            {
                _ctx.WriteLog(rule, keyPath, "(默认)", null, progId, "Restore", "failed", origin,
                    label + ": " + ex.Message);
            }
        }
    }
}
