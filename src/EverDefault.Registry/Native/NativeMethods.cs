using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EverDefault.Registry.Native
{
    internal static class NativeMethods
    {
        internal const int ERROR_SUCCESS = 0;

        internal const int REG_NOTIFY_CHANGE_NAME = 0x00000001;
        internal const int REG_NOTIFY_CHANGE_ATTRIBUTES = 0x00000002;
        internal const int REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
        internal const int REG_NOTIFY_CHANGE_SECURITY = 0x00000008;

        // KEY_SET_INFORMATION_CLASS
        internal const int KEY_WRITE_TIME_INFORMATION = 0;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegQueryInfoKey(
            SafeRegistryHandle hKey,
            IntPtr lpClass,
            IntPtr lpcchClass,
            IntPtr lpReserved,
            IntPtr lpcSubKeys,
            IntPtr lpcbMaxSubKeyLen,
            IntPtr lpcbMaxClassLen,
            IntPtr lpcValues,
            IntPtr lpcbMaxValueNameLen,
            IntPtr lpcbMaxValueLen,
            IntPtr lpcbSecurityDescriptor,
            out long lpftLastWriteTime);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern int RegNotifyChangeKeyValue(
            SafeRegistryHandle hKey,
            [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
            int dwNotifyFilter,
            IntPtr hEvent,
            [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

        [DllImport("ntdll.dll")]
        internal static extern int NtSetInformationKey(
            SafeRegistryHandle keyHandle,
            int keySetInformationClass,
            ref long keyInformation,
            int keyInformationLength);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        internal static extern int AssocQueryString(
            int flags,
            int str,
            string pszAssoc,
            string pszExtra,
            System.Text.StringBuilder pszOut,
            ref int pcchOut);

        internal const int ASSOCF_NONE = 0x00000000;
        internal const int ASSOCF_VERIFY = 0x00000040;

        internal const int ASSOCSTR_COMMAND = 1;
        internal const int ASSOCSTR_EXECUTABLE = 2;
    }
}
