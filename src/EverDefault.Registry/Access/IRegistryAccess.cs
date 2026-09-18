using System.Collections.Generic;

namespace EverDefault.Registry.Access
{
    public interface IRegistryAccess
    {
        bool KeyExists(string fullKeyPath);

        RegistryValueData ReadValue(string fullKeyPath, string valueName);

        void WriteValue(string fullKeyPath, string valueName, string kind, string data);

        void DeleteValue(string fullKeyPath, string valueName);

        void DeleteKey(string fullKeyPath, bool recursive);

        IReadOnlyList<RegistryValueData> EnumerateValues(string fullKeyPath);

        /// <summary>Enumerates subkey paths (canonical, starting at the hive) under a key.</summary>
        IReadOnlyList<string> EnumerateSubKeys(string fullKeyPath, bool recursive);

        long GetKeyLastWriteTime(string fullKeyPath);
    }
}
