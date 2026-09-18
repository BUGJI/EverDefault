using System;

namespace EverDefault.Registry.Access
{
    /// <summary>
    /// A normalized registry path split into hive and subkey, e.g.
    /// "HKEY_CURRENT_USER\Software\Foo" =&gt; Hive=CurrentUser, SubKey="Software\Foo".
    /// </summary>
    public sealed class RegPath
    {
        public RegistryHiveRoot Hive { get; private set; }

        public string SubKey { get; private set; }

        public string Canonical
        {
            get { return HiveName(Hive) + (string.IsNullOrEmpty(SubKey) ? string.Empty : "\\" + SubKey); }
        }

        public RegPath(RegistryHiveRoot hive, string subKey)
        {
            Hive = hive;
            SubKey = subKey ?? string.Empty;
        }

        public static RegPath Parse(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("Registry path is required.", nameof(fullPath));

            var path = fullPath.Trim();
            var separator = path.IndexOf('\\');
            var root = separator < 0 ? path : path.Substring(0, separator);
            var rest = separator < 0 ? string.Empty : path.Substring(separator + 1);

            return new RegPath(ParseHive(root), rest);
        }

        public static RegistryHiveRoot ParseHive(string root)
        {
            switch ((root ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "HKCU":
                case "HKEY_CURRENT_USER":
                    return RegistryHiveRoot.CurrentUser;
                case "HKLM":
                case "HKEY_LOCAL_MACHINE":
                    return RegistryHiveRoot.LocalMachine;
                case "HKU":
                case "HKEY_USERS":
                    return RegistryHiveRoot.Users;
                case "HKCR":
                case "HKEY_CLASSES_ROOT":
                    return RegistryHiveRoot.ClassesRoot;
                case "HKCC":
                case "HKEY_CURRENT_CONFIG":
                    return RegistryHiveRoot.CurrentConfig;
                default:
                    throw new ArgumentException("Unknown registry hive: " + root, nameof(root));
            }
        }

        public static string HiveName(RegistryHiveRoot hive)
        {
            switch (hive)
            {
                case RegistryHiveRoot.CurrentUser: return "HKEY_CURRENT_USER";
                case RegistryHiveRoot.LocalMachine: return "HKEY_LOCAL_MACHINE";
                case RegistryHiveRoot.Users: return "HKEY_USERS";
                case RegistryHiveRoot.ClassesRoot: return "HKEY_CLASSES_ROOT";
                case RegistryHiveRoot.CurrentConfig: return "HKEY_CURRENT_CONFIG";
                default: return "HKEY_CURRENT_USER";
            }
        }

        public override string ToString()
        {
            return Canonical;
        }
    }
}
