using System;
using System.Globalization;

namespace AcrylicUI.Core
{
    /// <summary>
    /// Domain names with characters outside ASCII, like a Spanish name written with its accents.
    ///
    /// On the wire, and therefore in everything Acrylic compares names with (the hosts file, the address cache), such
    /// a name only exists in its ASCII form, the one starting with "xn--". People type and paste the readable form,
    /// so wherever the UI takes a domain name it converts it.
    /// </summary>
    public static class DomainNames
    {
        private static readonly IdnMapping Mapping = new IdnMapping();

        public static bool IsAscii(string text)
        {
            foreach (char c in text)
            {
                if (c > 127) return false;
            }

            return true;
        }

        /// <summary>
        /// Converts every label having characters outside ASCII into its "xn--" form. Labels that cannot be converted
        /// (the ones holding the wildcards of a hosts pattern, for example) are left as they are.
        /// </summary>
        public static string ToAscii(string name)
        {
            if (string.IsNullOrEmpty(name) || IsAscii(name)) return name;

            string[] labels = name.Split('.');

            for (int i = 0; i < labels.Length; i++)
            {
                if (IsAscii(labels[i]) || labels[i].IndexOfAny(new[] { '*', '?' }) >= 0) continue;

                try
                {
                    labels[i] = Mapping.GetAscii(labels[i]);
                }
                catch (ArgumentException)
                {
                }
            }

            return string.Join(".", labels);
        }

        /// <summary>
        /// Converts a domain name of the hosts file, which may start with the '>' shortcut. Regular expressions are
        /// left alone.
        /// </summary>
        public static string ToAsciiHostsEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry) || entry[0] == '/') return entry;

            return entry[0] == '>' ? ">" + ToAscii(entry.Substring(1)) : ToAscii(entry);
        }

        /// <summary>
        /// Returns the readable form of a name written in its "xn--" form, or the name itself.
        /// </summary>
        public static string ToUnicode(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf("xn--", StringComparison.OrdinalIgnoreCase) < 0) return name;

            try
            {
                return Mapping.GetUnicode(name);
            }
            catch (ArgumentException)
            {
                return name;
            }
        }
    }
}
