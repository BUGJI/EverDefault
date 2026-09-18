using System;
using System.Text;
using System.Text.RegularExpressions;

namespace EverDefault.Core.Util
{
    /// <summary>Wildcard matching for registry paths ('*' = any chars, '?' = one char).</summary>
    public static class Wildcard
    {
        public static bool IsMatch(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.IsNullOrEmpty(input);

            if (string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(input ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Returns the fixed prefix of a wildcard pattern (up to the first wildcard).</summary>
        public static string StaticPrefix(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return string.Empty;

            var index = pattern.IndexOfAny(new[] { '*', '?' });
            if (index < 0)
                return pattern;

            var prefix = pattern.Substring(0, index);
            var slash = prefix.LastIndexOf('\\');
            return slash < 0 ? string.Empty : prefix.Substring(0, slash);
        }

        public static bool HasWildcard(string pattern)
        {
            return !string.IsNullOrEmpty(pattern) && pattern.IndexOfAny(new[] { '*', '?' }) >= 0;
        }

        public static string ToRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            foreach (var c in pattern ?? string.Empty)
            {
                if (c == '*') builder.Append(".*");
                else if (c == '?') builder.Append('.');
                else builder.Append(Regex.Escape(c.ToString()));
            }
            builder.Append("$");
            return builder.ToString();
        }
    }
}
