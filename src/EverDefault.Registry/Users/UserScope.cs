using System;
using System.Collections.Generic;
using System.Security.Principal;
using EverDefault.Core.Model;
using EverDefault.Registry.Access;
using Win32 = Microsoft.Win32;

namespace EverDefault.Registry.Users
{
    /// <summary>
    /// Resolves per-user work when hosted as a service (SYSTEM has no meaningful HKCU).
    /// All operations go through HKEY_USERS\&lt;SID&gt; so the same code path works for an
    /// interactive user and for a service enforcing rules for logged-on users.
    /// </summary>
    public sealed class UserScope
    {
        private readonly List<string> _sids = new List<string>();

        public UserScope(UserScopeMode mode)
        {
            IsSystem = IsLocalSystem();

            if (mode == UserScopeMode.CurrentUser)
            {
                var sid = CurrentSid();
                if (sid != null)
                    _sids.Add(sid);
                return;
            }

            foreach (var sid in EnumerateLoadedUserSids())
                _sids.Add(sid);

            // Never come up empty in an interactive/session context.
            if (_sids.Count == 0)
            {
                var sid = CurrentSid();
                if (sid != null)
                    _sids.Add(sid);
            }
        }

        public bool IsSystem { get; private set; }

        public IReadOnlyList<string> Sids
        {
            get { return _sids; }
        }

        public Win32.RegistryKey OpenHive(string sid, bool writable = false)
        {
            return Win32.Registry.Users.OpenSubKey(sid, writable);
        }

        /// <summary>Expands an HKCU path into one HKEY_USERS\&lt;SID&gt; path per target user.</summary>
        public IEnumerable<string> ExpandCurrentUser(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                yield break;

            RegPath parsed = null;
            try
            {
                parsed = RegPath.Parse(path);
            }
            catch (ArgumentException)
            {
                parsed = null;
            }

            if (parsed == null)
            {
                yield return path;
                yield break;
            }

            if (parsed.Hive != RegistryHiveRoot.CurrentUser)
            {
                yield return parsed.Canonical;
                yield break;
            }

            var users = RegPath.HiveName(RegistryHiveRoot.Users);
            foreach (var sid in _sids)
            {
                var root = users + "\\" + sid;
                yield return string.IsNullOrEmpty(parsed.SubKey) ? root : root + "\\" + parsed.SubKey;
            }
        }

        public static string CurrentSid()
        {
            try
            {
                var user = WindowsIdentity.GetCurrent().User;
                return user == null ? null : user.Value;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsLocalSystem()
        {
            try
            {
                var user = WindowsIdentity.GetCurrent().User;
                return user != null && user.IsWellKnown(WellKnownSidType.LocalSystemSid);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IEnumerable<string> EnumerateLoadedUserSids()
        {
            var users = Win32.Registry.Users;
            foreach (var name in users.GetSubKeyNames())
            {
                if (!name.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Built-in accounts (Administrator/Guest/DefaultAccount) are not interactive users.
                if (name.EndsWith("-500", StringComparison.Ordinal) ||
                    name.EndsWith("-501", StringComparison.Ordinal) ||
                    name.EndsWith("-503", StringComparison.Ordinal) ||
                    name.EndsWith("-504", StringComparison.Ordinal))
                    continue;

                using (var probe = users.OpenSubKey(name + @"\Software\Microsoft\Windows\CurrentVersion\Explorer"))
                {
                    if (probe != null)
                        yield return name;
                }
            }
        }
    }
}
