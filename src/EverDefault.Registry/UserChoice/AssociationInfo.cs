using System.Text;
using EverDefault.Registry.Native;

namespace EverDefault.Registry.UserChoice
{
    /// <summary>
    /// Thin wrapper over AssocQueryString. With ASSOCF_VERIFY the shell validates the
    /// UserChoice hash, so a mismatch means Windows is ignoring our restored key.
    /// This is the differential test that proves whether the restore really worked.
    /// </summary>
    public static class AssociationInfo
    {
        public static string ResolveExecutable(string assoc, bool verify)
        {
            return Query(NativeMethods.ASSOCSTR_EXECUTABLE, assoc, verify);
        }

        public static string ResolveCommand(string assoc, bool verify)
        {
            return Query(NativeMethods.ASSOCSTR_COMMAND, assoc, verify);
        }

        private static string Query(int str, string assoc, bool verify)
        {
            if (string.IsNullOrEmpty(assoc))
                return null;

            int flags = verify ? NativeMethods.ASSOCF_VERIFY : NativeMethods.ASSOCF_NONE;

            int size = 0;
            NativeMethods.AssocQueryString(flags, str, assoc, null, null, ref size);
            if (size <= 0)
                return null;

            var buffer = new StringBuilder(size);
            int hr = NativeMethods.AssocQueryString(flags, str, assoc, null, buffer, ref size);
            return hr == 0 ? buffer.ToString() : null;
        }
    }
}
