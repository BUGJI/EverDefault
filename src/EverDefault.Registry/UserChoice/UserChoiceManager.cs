using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using EverDefault.Registry.Native;
using Microsoft.Win32;
using Win32Registry = Microsoft.Win32.Registry;

namespace EverDefault.Registry.UserChoice
{
    /// <summary>Which UserChoice family a manager operates on.</summary>
    public enum UserChoiceKind
    {
        /// <summary>File extensions, under Explorer\FileExts.</summary>
        FileExtension = 0,

        /// <summary>URL protocols (http/https/...), under Shell\Associations\UrlAssociations.</summary>
        UrlProtocol = 1
    }

    /// <summary>
    /// Reads, deletes and restores the per-user UserChoice key used by Windows to
    /// store the default application for a file extension.
    ///
    /// Restore strategy (approach A - snapshot replay):
    ///   rather than re-implementing the undocumented UserChoice hash algorithm, we
    ///   replay the exact triple (ProgId, Hash, key LastWriteTime) captured from a
    ///   valid Windows state. Windows re-validates the hash against the key's last
    ///   write time, so all three must match.
    /// </summary>
    public sealed class UserChoiceManager
    {
        private const string FileExtsBase =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

        private const string UrlAssociationsBase =
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations";

        private readonly RegistryKey _hive;
        private readonly string _userSid;
        private readonly UserChoiceKind _kind;

        public UserChoiceManager()
            : this(Win32Registry.CurrentUser, null, UserChoiceKind.FileExtension)
        {
        }

        /// <summary>
        /// Allows the same logic to run against a specific user hive (HKEY_USERS\SID)
        /// when hosted inside the service.
        /// </summary>
        public UserChoiceManager(RegistryKey hive)
            : this(hive, null, UserChoiceKind.FileExtension)
        {
        }

        /// <summary>
        /// <paramref name="userSid"/> keeps the real user in the key's DACL when the
        /// service (SYSTEM) has to take ownership of a protected UserChoice key.
        /// </summary>
        public UserChoiceManager(RegistryKey hive, string userSid)
            : this(hive, userSid, UserChoiceKind.FileExtension)
        {
        }

        public UserChoiceManager(RegistryKey hive, string userSid, UserChoiceKind kind)
        {
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _userSid = userSid;
            _kind = kind;
        }

        private string BasePath
        {
            get { return _kind == UserChoiceKind.UrlProtocol ? UrlAssociationsBase : FileExtsBase; }
        }

        /// <summary>Normalizes the target name according to the manager's kind.</summary>
        private string Normalize(string name)
        {
            return _kind == UserChoiceKind.UrlProtocol
                ? NormalizeProtocol(name)
                : NormalizeExtension(name);
        }

        /// <summary>Normalizes a URL protocol to lower-case, dot-free form ("http").</summary>
        public static string NormalizeProtocol(string protocol)
        {
            if (string.IsNullOrWhiteSpace(protocol))
                throw new ArgumentException("protocol is required", nameof(protocol));

            var value = protocol.Trim().TrimEnd(':').Trim().ToLowerInvariant();
            if (value.Length == 0)
                throw new ArgumentException("protocol is required", nameof(protocol));

            return value;
        }

        public static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                throw new ArgumentException("extension is required", nameof(extension));

            extension = extension.Trim();
            if (extension[0] != '.')
                extension = "." + extension;

            return extension;
        }

        public string GetExtensionKeyPath(string extension)
        {
            return BasePath + "\\" + Normalize(extension);
        }

        public string GetUserChoiceKeyPath(string extension)
        {
            return GetExtensionKeyPath(extension) + "\\UserChoice";
        }

        public UserChoiceSnapshot Capture(string extension)
        {
            extension = Normalize(extension);

            using (var key = _hive.OpenSubKey(GetUserChoiceKeyPath(extension), false))
            {
                if (key == null)
                {
                    return new UserChoiceSnapshot
                    {
                        Extension = extension,
                        Exists = false,
                        CapturedUtc = DateTime.UtcNow
                    };
                }

                long lastWriteTime = ReadLastWriteTime(key);

                return new UserChoiceSnapshot
                {
                    Extension = extension,
                    Exists = true,
                    ProgId = key.GetValue("ProgId", null) as string,
                    Hash = key.GetValue("Hash", null) as string,
                    LastWriteTimeUtc = lastWriteTime,
                    CapturedUtc = DateTime.UtcNow
                };
            }
        }

        public static long ReadLastWriteTime(RegistryKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            long lastWriteTime;
            int rc = NativeMethods.RegQueryInfoKey(
                key.Handle,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                out lastWriteTime);

            if (rc != NativeMethods.ERROR_SUCCESS)
                throw new Win32Exception(rc, "RegQueryInfoKey failed");

            return lastWriteTime;
        }

        /// <summary>
        /// Deletes the UserChoice key. This is the "tamper" we guard against: Windows
        /// then falls back to the legacy association or prompts the user.
        /// </summary>
        public bool Delete(string extension)
        {
            extension = Normalize(extension);
            var extPath = GetExtensionKeyPath(extension);

            using (var extKey = _hive.OpenSubKey(extPath, true))
            {
                if (extKey == null)
                    return false;

                using (var child = extKey.OpenSubKey("UserChoice", false))
                {
                    if (child == null)
                        return false;
                }

                try
                {
                    extKey.DeleteSubKeyTree("UserChoice", false);
                }
                catch (UnauthorizedAccessException)
                {
                    EnsureUserChoiceWritable(extKey);
                    extKey.DeleteSubKeyTree("UserChoice", false);
                }

                return true;
            }
        }

        public bool SetLastWriteTime(string extension, long lastWriteTimeUtc)
        {
            extension = Normalize(extension);

            using (var ucKey = _hive.OpenSubKey(
                GetUserChoiceKeyPath(extension),
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.WriteKey | RegistryRights.ReadKey))
            {
                if (ucKey == null)
                    return false;

                long ft = lastWriteTimeUtc;
                int status = NativeMethods.NtSetInformationKey(
                    ucKey.Handle,
                    NativeMethods.KEY_WRITE_TIME_INFORMATION,
                    ref ft,
                    sizeof(long));

                return status == 0;
            }
        }

        public RestoreResult Restore(UserChoiceSnapshot snapshot, bool setLastWriteTime = true)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            if (!snapshot.Exists)
                return RestoreResult.Create(false, false, "snapshot has no data to restore");

            if (snapshot.ProgId == null || snapshot.Hash == null)
                return RestoreResult.Create(false, false, "snapshot is missing ProgId/Hash");

            var extension = Normalize(snapshot.Extension);
            var extPath = GetExtensionKeyPath(extension);
            var ucPath = extPath + "\\UserChoice";

            using (var extKey = _hive.CreateSubKey(extPath))
            {
                if (extKey == null)
                    return RestoreResult.Create(false, false, "could not create extension key: " + extPath);

                EnsureUserChoiceWritable(extKey);

                try
                {
                    extKey.DeleteSubKeyTree("UserChoice", false);
                }
                catch (UnauthorizedAccessException)
                {
                    return RestoreResult.Create(false, false, "UserChoice exists but could not be deleted");
                }

                try
                {
                    using (var ucKey = extKey.CreateSubKey("UserChoice", RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (ucKey == null)
                            return RestoreResult.Create(false, false, "failed to create UserChoice key");

                        ucKey.SetValue("ProgId", snapshot.ProgId, RegistryValueKind.String);
                        ucKey.SetValue("Hash", snapshot.Hash, RegistryValueKind.String);
                        ucKey.Flush();
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Safety net: a freshly created key should never be denied, but repair and retry.
                    EnsureUserChoiceWritable(extKey);
                    using (var ucKey = extKey.OpenSubKey("UserChoice", RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (ucKey == null)
                            return RestoreResult.Create(false, false, "UserChoice not writable even after ACL repair");

                        ucKey.SetValue("ProgId", snapshot.ProgId, RegistryValueKind.String);
                        ucKey.SetValue("Hash", snapshot.Hash, RegistryValueKind.String);
                        ucKey.Flush();
                    }
                }
            }

            if (!setLastWriteTime)
                return RestoreResult.Create(true, false, null);

            using (var ucKey = _hive.OpenSubKey(
                ucPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.WriteKey | RegistryRights.SetValue | RegistryRights.ReadKey))
            {
                if (ucKey == null)
                    return RestoreResult.Create(true, false, "values restored but could not reopen key to set write time");

                long ft = snapshot.LastWriteTimeUtc;
                int status = NativeMethods.NtSetInformationKey(
                    ucKey.Handle,
                    NativeMethods.KEY_WRITE_TIME_INFORMATION,
                    ref ft,
                    sizeof(long));

                if (status != 0)
                    return RestoreResult.Create(true, false, "NtSetInformationKey failed, NTSTATUS=0x" + status.ToString("X8"));

                return RestoreResult.Create(true, true, null);
            }
        }

        /// <summary>
        /// Writes a brand new association by replaying a freshly computed ProgId/Hash pair.
        /// Used when actively *setting* a default rather than restoring a snapshot.
        /// </summary>
        public RestoreResult WriteRaw(string extension, string progId, string hash, long lastWriteTimeUtc)
        {
            return Restore(
                new UserChoiceSnapshot
                {
                    Extension = Normalize(extension),
                    Exists = true,
                    ProgId = progId,
                    Hash = hash,
                    LastWriteTimeUtc = lastWriteTimeUtc
                },
                true);
        }

        public IReadOnlyList<string> ListExtensionsWithUserChoice()
        {
            var result = new List<string>();

            using (var extRoot = _hive.OpenSubKey(BasePath, false))
            {
                if (extRoot == null)
                    return result;

                foreach (var name in extRoot.GetSubKeyNames())
                {
                    using (var uc = extRoot.OpenSubKey(name + "\\UserChoice", false))
                    {
                        if (uc != null)
                            result.Add(name);
                    }
                }
            }

            return result;
        }

        public string ReadCurrentProgId(string extension)
        {
            var snapshot = Capture(extension);
            return snapshot.Exists ? snapshot.ProgId : null;
        }

        /// <summary>
        /// Removes the Windows-imposed DENY ace from the protected UserChoice key and
        /// grants the current user full control, taking ownership if required.
        /// Must be called before deleting or rewriting an existing, protected key.
        /// </summary>
        private void EnsureUserChoiceWritable(RegistryKey extKey)
        {
            using (var probe = extKey.OpenSubKey("UserChoice", false))
            {
                if (probe == null)
                    return; // nothing exists yet; a new key inherits normal rights
            }

            var user = WindowsIdentity.GetCurrent().User;

            using (var ucKey = extKey.OpenSubKey(
                "UserChoice",
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.TakeOwnership | RegistryRights.ChangePermissions | RegistryRights.ReadPermissions))
            {
                if (ucKey == null)
                    throw new UnauthorizedAccessException("Cannot open UserChoice to repair its ACL.");

                var security = ucKey.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                security.SetOwner(user);

                var denyRules = new List<RegistryAccessRule>();
                foreach (RegistryAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    if (rule.AccessControlType == AccessControlType.Deny)
                        denyRules.Add(rule);
                }

                foreach (var rule in denyRules)
                    security.RemoveAccessRule(rule);

                security.AddAccessRule(new RegistryAccessRule(user, RegistryRights.FullControl, AccessControlType.Allow));

                if (!string.IsNullOrEmpty(_userSid))
                {
                    try
                    {
                        var actualUser = new SecurityIdentifier(_userSid);
                        security.AddAccessRule(new RegistryAccessRule(
                            actualUser, RegistryRights.FullControl, AccessControlType.Allow));
                    }
                    catch (Exception)
                    {
                        // Non-fatal: the caller still has access, the user may not.
                    }
                }

                ucKey.SetAccessControl(security);
            }
        }
    }
}
