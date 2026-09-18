using System;
using System.Runtime.InteropServices;
using EverDefault.Core.Model;
using Microsoft.Win32;

namespace EverDefault.Core.Os
{
    public sealed class OsInfo
    {
        public int Major { get; private set; }

        public int Minor { get; private set; }

        public int Build { get; private set; }

        public string ProductName { get; private set; }

        public OsFamily Family
        {
            get
            {
                if (Major == 6 && Minor == 1) return OsFamily.Win7;
                if (Major == 6 && (Minor == 2 || Minor == 3)) return OsFamily.Win8;
                if (Major == 10 && Build >= 22000) return OsFamily.Win11;
                if (Major == 10) return OsFamily.Win10;
                return OsFamily.Unknown;
            }
        }

        /// <summary>True when this OS protects UserChoice with the hash mechanism.</summary>
        public bool RequiresUserChoiceHash
        {
            get { return Family == OsFamily.Win10 || Family == OsFamily.Win11; }
        }

        public static OsInfo Detect()
        {
            var info = new RTL_OSVERSIONINFOW();
            info.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOW));

            try
            {
                RtlGetVersion(ref info);
            }
            catch (DllNotFoundException)
            {
            }

            var os = new OsInfo
            {
                Major = (int)info.dwMajorVersion,
                Minor = (int)info.dwMinorVersion,
                Build = (int)info.dwBuildNumber,
                ProductName = ReadProductName()
            };

            return os;
        }

        private static string ReadProductName()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (key != null)
                        return key.GetValue("ProductName") as string;
                }
            }
            catch
            {
                // ignored - product name is informational only
            }

            return null;
        }

        public override string ToString()
        {
            return string.Format("{0} ({1}.{2}.{3}, {4})", ProductName, Major, Minor, Build, Family);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RTL_OSVERSIONINFOW
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW lpVersionInformation);
    }
}
