using System;
using System.Collections.Generic;
using EverDefault.Registry.UserChoice;
using Win32 = Microsoft.Win32;

namespace EverDefault.Registry.Access
{
    /// <summary>
    /// Default <see cref="IRegistryAccess"/> over the 64-bit registry view.
    /// NOTE: when hosted inside the service, HKEY_CURRENT_USER is the service account;
    /// per-user work must target HKEY_USERS\&lt;SID&gt; via <see cref="RegPath"/>.
    /// </summary>
    public sealed class RegistryAccess : IRegistryAccess
    {
        private readonly Win32.RegistryView _view;

        public RegistryAccess()
            : this(Win32.RegistryView.Registry64)
        {
        }

        public RegistryAccess(Win32.RegistryView view)
        {
            _view = view;
        }

        public bool KeyExists(string fullKeyPath)
        {
            using (var key = OpenKey(fullKeyPath, false))
                return key != null;
        }

        public RegistryValueData ReadValue(string fullKeyPath, string valueName)
        {
            using (var key = OpenKey(fullKeyPath, false))
            {
                if (key == null)
                    return RegistryValueData.Missing(fullKeyPath, valueName);

                var names = key.GetValueNames();
                var exists = Array.Exists(names, n => string.Equals(n, valueName ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase));

                if (!exists)
                    return RegistryValueData.Missing(fullKeyPath, valueName);

                var kind = key.GetValueKind(valueName ?? string.Empty);
                var raw = key.GetValue(valueName ?? string.Empty, null, Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
                var lastWrite = UserChoiceManager.ReadLastWriteTime(key);

                return new RegistryValueData
                {
                    KeyPath = fullKeyPath,
                    Name = valueName,
                    Exists = true,
                    Kind = kind.ToString(),
                    Data = RegistryDataConverter.Encode(raw, kind),
                    KeyLastWriteTimeUtc = lastWrite
                };
            }
        }

        public void WriteValue(string fullKeyPath, string valueName, string kind, string data)
        {
            var path = RegPath.Parse(fullKeyPath);
            var valueKind = RegistryDataConverter.ParseKind(kind);
            var value = RegistryDataConverter.Decode(data, valueKind);

            using (var baseKey = GetBaseKey(path.Hive, true))
            using (var key = string.IsNullOrEmpty(path.SubKey)
                ? baseKey
                : baseKey.CreateSubKey(path.SubKey))
            {
                if (key == null)
                    throw new InvalidOperationException("Cannot open/create key: " + fullKeyPath);

                key.SetValue(valueName ?? string.Empty, value, valueKind);
            }
        }

        public void DeleteValue(string fullKeyPath, string valueName)
        {
            using (var key = OpenKey(fullKeyPath, true))
            {
                if (key == null)
                    return;

                key.DeleteValue(valueName ?? string.Empty, false);
            }
        }

        public void DeleteKey(string fullKeyPath, bool recursive)
        {
            var path = RegPath.Parse(fullKeyPath);
            if (string.IsNullOrEmpty(path.SubKey))
                return;

            var parent = path.SubKey;
            var slash = parent.LastIndexOf('\\');
            var leaf = slash < 0 ? parent : parent.Substring(slash + 1);
            var parentPath = slash < 0 ? string.Empty : parent.Substring(0, slash);

            using (var baseKey = GetBaseKey(path.Hive, true))
            {
                var parentKey = string.IsNullOrEmpty(parentPath) ? baseKey : baseKey.OpenSubKey(parentPath, true);
                if (parentKey == null)
                    return;

                try
                {
                    if (recursive)
                        parentKey.DeleteSubKeyTree(leaf, false);
                    else
                        parentKey.DeleteSubKey(leaf, false);
                }
                finally
                {
                    if (!ReferenceEquals(parentKey, baseKey))
                        parentKey.Close();
                }
            }
        }

        public IReadOnlyList<RegistryValueData> EnumerateValues(string fullKeyPath)
        {
            var result = new List<RegistryValueData>();

            using (var key = OpenKey(fullKeyPath, false))
            {
                if (key == null)
                    return result;

                var lastWrite = UserChoiceManager.ReadLastWriteTime(key);
                foreach (var name in key.GetValueNames())
                {
                    var kind = key.GetValueKind(name);
                    var raw = key.GetValue(name, null, Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
                    result.Add(new RegistryValueData
                    {
                        KeyPath = fullKeyPath,
                        Name = name,
                        Exists = true,
                        Kind = kind.ToString(),
                        Data = RegistryDataConverter.Encode(raw, kind),
                        KeyLastWriteTimeUtc = lastWrite
                    });
                }
            }

            return result;
        }

        public IReadOnlyList<string> EnumerateSubKeys(string fullKeyPath, bool recursive)
        {
            var result = new List<string>();

            using (var key = OpenKey(fullKeyPath, false))
            {
                if (key == null)
                    return result;

                Enumerate(key, fullKeyPath, recursive, result);
            }

            return result;
        }

        public long GetKeyLastWriteTime(string fullKeyPath)
        {
            using (var key = OpenKey(fullKeyPath, false))
            {
                return key == null ? 0 : UserChoiceManager.ReadLastWriteTime(key);
            }
        }

        private static void Enumerate(Win32.RegistryKey key, string path, bool recursive, List<string> output)
        {
            foreach (var name in key.GetSubKeyNames())
            {
                var childPath = path + "\\" + name;
                output.Add(childPath);

                if (!recursive)
                    continue;

                using (var child = key.OpenSubKey(name, false))
                {
                    if (child != null)
                        Enumerate(child, childPath, true, output);
                }
            }
        }

        private Win32.RegistryKey OpenKey(string fullKeyPath, bool writable)
        {
            var path = RegPath.Parse(fullKeyPath);
            var baseKey = GetBaseKey(path.Hive, writable);
            if (string.IsNullOrEmpty(path.SubKey))
                return baseKey;

            var key = baseKey.OpenSubKey(path.SubKey, writable);
            baseKey.Close();
            return key;
        }

        private Win32.RegistryKey GetBaseKey(RegistryHiveRoot hive, bool writable)
        {
            switch (hive)
            {
                case RegistryHiveRoot.CurrentUser:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.CurrentUser, _view);
                case RegistryHiveRoot.LocalMachine:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.LocalMachine, _view);
                case RegistryHiveRoot.Users:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.Users, _view);
                case RegistryHiveRoot.ClassesRoot:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.ClassesRoot, _view);
                case RegistryHiveRoot.CurrentConfig:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.CurrentConfig, _view);
                default:
                    return Win32.RegistryKey.OpenBaseKey(Win32.RegistryHive.CurrentUser, _view);
            }
        }
    }
}
