using System;
using Win32 = Microsoft.Win32;

namespace EverDefault.Registry.Access
{
    /// <summary>Converts registry values to and from a portable string representation.</summary>
    public static class RegistryDataConverter
    {
        public static string Encode(object value, Win32.RegistryValueKind kind)
        {
            if (value == null)
                return null;

            switch (kind)
            {
                case Win32.RegistryValueKind.String:
                case Win32.RegistryValueKind.ExpandString:
                    return value as string;

                case Win32.RegistryValueKind.DWord:
                    return Convert.ToInt32(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

                case Win32.RegistryValueKind.QWord:
                    return Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

                case Win32.RegistryValueKind.MultiString:
                    return string.Join("\n", (string[])value);

                case Win32.RegistryValueKind.Binary:
                    return Convert.ToBase64String((byte[])value);

                default:
                    return value.ToString();
            }
        }

        public static object Decode(string data, Win32.RegistryValueKind kind)
        {
            switch (kind)
            {
                case Win32.RegistryValueKind.String:
                case Win32.RegistryValueKind.ExpandString:
                    return data ?? string.Empty;

                case Win32.RegistryValueKind.DWord:
                    return int.Parse(data ?? "0", System.Globalization.CultureInfo.InvariantCulture);

                case Win32.RegistryValueKind.QWord:
                    return long.Parse(data ?? "0", System.Globalization.CultureInfo.InvariantCulture);

                case Win32.RegistryValueKind.MultiString:
                    return data == null ? new string[0] : data.Split('\n');

                case Win32.RegistryValueKind.Binary:
                    return string.IsNullOrEmpty(data) ? new byte[0] : Convert.FromBase64String(data);

                default:
                    return data;
            }
        }

        public static Win32.RegistryValueKind ParseKind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return Win32.RegistryValueKind.String;

            Win32.RegistryValueKind parsed;
            return Enum.TryParse(kind, true, out parsed) ? parsed : Win32.RegistryValueKind.String;
        }
    }
}
