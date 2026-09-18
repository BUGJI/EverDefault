using System;
using System.Runtime.InteropServices;

namespace EverDefault.Registry.Native
{
    /// <summary>Notifies the shell that associations changed (optional, off by default).</summary>
    public static class ShellRefresh
    {
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void NotifyAssociationsChanged()
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
