using System;
using System.Collections.Generic;
using EverDefault.Registry.Access;

namespace EverDefault.Registry.NameSpace
{
    public sealed class NameSpaceEntry
    {
        public string NameSpaceKeyPath { get; set; }

        public string Guid { get; set; }

        public string ClsidKeyPath { get; set; }

        public string DisplayName { get; set; }

        public string TargetPath { get; set; }

        public override string ToString()
        {
            return string.Format("{0} [{1}] {2}", Guid, DisplayName, NameSpaceKeyPath);
        }
    }

    /// <summary>Scans the well-known "This PC" / Desktop namespace roots for CLSID entries.</summary>
    public static class NameSpaceScanner
    {
        public static readonly IReadOnlyList<string> DefaultRoots = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace"
        };

        public static IReadOnlyList<NameSpaceEntry> Scan(
            IRegistryAccess access, IEnumerable<string> roots = null)
        {
            var result = new List<NameSpaceEntry>();

            foreach (var root in roots ?? DefaultRoots)
            {
                if (!access.KeyExists(root))
                    continue;

                foreach (var nameSpacePath in access.EnumerateSubKeys(root, false))
                {
                    var guid = nameSpacePath.Substring(nameSpacePath.LastIndexOf('\\') + 1);
                    if (guid.Length < 3 || guid[0] != '{')
                        continue;

                    var display = access.ReadValue(nameSpacePath, string.Empty);
                    var clsidPath = FindClsidPath(access, guid);
                    var target = FindTargetPath(access, clsidPath);

                    result.Add(new NameSpaceEntry
                    {
                        NameSpaceKeyPath = nameSpacePath,
                        Guid = guid,
                        ClsidKeyPath = clsidPath,
                        DisplayName = display.Exists ? display.Data : null,
                        TargetPath = target
                    });
                }
            }

            return result;
        }

        public static string FindClsidPath(IRegistryAccess access, string guid)
        {
            var candidates = new[]
            {
                @"HKEY_CURRENT_USER\Software\Classes\CLSID\" + guid,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\" + guid,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Classes\CLSID\" + guid
            };

            foreach (var candidate in candidates)
            {
                if (access.KeyExists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string FindTargetPath(IRegistryAccess access, string clsidPath)
        {
            if (string.IsNullOrEmpty(clsidPath))
                return null;

            var probe = access.ReadValue(clsidPath + @"\Instance\InitPropertyBag", "Target");
            if (probe.Exists)
                return probe.Data;

            var icon = access.ReadValue(clsidPath, "DefaultIcon");
            return icon.Exists ? icon.Data : null;
        }
    }
}
