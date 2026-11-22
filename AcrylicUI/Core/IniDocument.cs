using System;
using System.Collections.Generic;
using System.Text;

namespace AcrylicUI.Core
{
    public enum IniLineKind
    {
        Blank,
        Comment,
        Section,
        KeyValue,
        Other
    }

    public sealed class IniLine
    {
        public string Raw;
        public string Terminator; // The line terminator found in the file, null for a line added by the UI.
        public IniLineKind Kind;
        public string Section; // The section the line belongs to (or declares), null before the first section.
        public string Key;
        public string Value;
    }

    /// <summary>
    /// A line-preserving model of AcrylicConfiguration.ini.
    ///
    /// The file is interpreted exactly the way the Acrylic service does it (through Delphi's TMemIniFile): lines are
    /// trimmed, a line starting with ';' is a comment, section and key names are case-insensitive, the value is the
    /// trimmed text following the first '=' and the first occurrence of a section or of a key wins.
    ///
    /// Changing a value rewrites only the text following the '=' of that one line, so comments, ordering and spacing
    /// of everything else survive a save untouched.
    /// </summary>
    public sealed class IniDocument
    {
        private readonly List<IniLine> lines = new List<IniLine>();

        private string newLine = "\r\n";
        private bool endsWithNewLine = true;

        public IReadOnlyList<IniLine> Lines => lines;

        public static IniDocument Parse(string text)
        {
            var document = new IniDocument();

            text = text ?? "";

            document.newLine = TextFile.DetectNewLine(text);
            document.endsWithNewLine = text.Length == 0 || text.EndsWith("\n", StringComparison.Ordinal);

            string currentSection = null;

            foreach (KeyValuePair<string, string> rawLine in TextFile.SplitLines(text))
            {
                IniLine line = Classify(rawLine.Key, currentSection);

                line.Terminator = rawLine.Value;

                if (line.Kind == IniLineKind.Section) currentSection = line.Section;

                document.lines.Add(line);
            }

            return document;
        }

        private static IniLine Classify(string raw, string currentSection)
        {
            var line = new IniLine { Raw = raw, Section = currentSection };

            string trimmed = raw.Trim();

            if (trimmed.Length == 0)
            {
                line.Kind = IniLineKind.Blank;
            }
            else if (trimmed[0] == ';')
            {
                line.Kind = IniLineKind.Comment;
            }
            else if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                line.Kind = IniLineKind.Section;
                line.Section = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }
            else
            {
                int equalsAt = trimmed.IndexOf('=');

                if (equalsAt > 0 && currentSection != null)
                {
                    line.Kind = IniLineKind.KeyValue;
                    line.Key = trimmed.Substring(0, equalsAt).Trim();
                    line.Value = trimmed.Substring(equalsAt + 1).Trim();
                }
                else
                {
                    line.Kind = IniLineKind.Other;
                }
            }

            return line;
        }

        public string ToText()
        {
            var builder = new StringBuilder();

            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append(lines[i].Raw);
                builder.Append(TextFile.TerminatorFor(lines[i].Terminator, i == lines.Count - 1, newLine, endsWithNewLine));
            }

            return builder.ToString();
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the bounds [first, last) of the body of the first section having the specified name, or false.
        /// </summary>
        private bool TryGetSectionBody(string section, out int first, out int last)
        {
            first = -1; last = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Kind == IniLineKind.Section && SameName(lines[i].Section, section))
                {
                    first = i + 1; last = lines.Count;

                    for (int k = first; k < lines.Count; k++)
                    {
                        if (lines[k].Kind == IniLineKind.Section) { last = k; break; }
                    }

                    return true;
                }
            }

            return false;
        }

        private IniLine FindKey(string section, string key)
        {
            if (!TryGetSectionBody(section, out int first, out int last)) return null;

            for (int i = first; i < last; i++)
            {
                if (lines[i].Kind == IniLineKind.KeyValue && SameName(lines[i].Key, key)) return lines[i];
            }

            return null;
        }

        /// <summary>
        /// Returns the value of the specified key, or null if the key is not present in the file.
        /// </summary>
        public string Get(string section, string key)
        {
            return FindKey(section, key)?.Value;
        }

        public bool Contains(string section, string key)
        {
            return FindKey(section, key) != null;
        }

        public void Set(string section, string key, string value)
        {
            value = (value ?? "").Trim();

            IniLine existing = FindKey(section, key);

            if (existing != null)
            {
                if (existing.Value == value) return;

                int equalsAt = existing.Raw.IndexOf('=');

                existing.Raw = existing.Raw.Substring(0, equalsAt + 1) + value;
                existing.Value = value;

                return;
            }

            if (!TryGetSectionBody(section, out _, out int last))
            {
                lines.Add(new IniLine { Raw = "[" + section + "]", Kind = IniLineKind.Section, Section = section });

                last = lines.Count;
            }

            lines.Insert(TrimTrailingBlankLines(last), NewKeyValue(section, key, value));
        }

        /// <summary>
        /// Returns the entries of a section in file order.
        /// </summary>
        public List<KeyValuePair<string, string>> GetEntries(string section)
        {
            var result = new List<KeyValuePair<string, string>>();

            if (!TryGetSectionBody(section, out int first, out int last)) return result;

            for (int i = first; i < last; i++)
            {
                if (lines[i].Kind == IniLineKind.KeyValue) result.Add(new KeyValuePair<string, string>(lines[i].Key, lines[i].Value));
            }

            return result;
        }

        /// <summary>
        /// Replaces every entry of a section with the specified ones, leaving the comments of the section in place.
        /// </summary>
        public void ReplaceEntries(string section, IList<KeyValuePair<string, string>> entries)
        {
            if (!TryGetSectionBody(section, out int first, out int last))
            {
                if (entries.Count == 0) return;

                lines.Add(new IniLine { Raw = "[" + section + "]", Kind = IniLineKind.Section, Section = section });

                first = lines.Count; last = lines.Count;
            }

            int insertAt = -1;

            for (int i = last - 1; i >= first; i--)
            {
                if (lines[i].Kind == IniLineKind.KeyValue)
                {
                    lines.RemoveAt(i); last--; insertAt = i;
                }
            }

            if (insertAt < 0) insertAt = TrimTrailingBlankLines(last);

            for (int i = 0; i < entries.Count; i++)
            {
                lines.Insert(insertAt + i, NewKeyValue(section, entries[i].Key, (entries[i].Value ?? "").Trim()));
            }
        }

        private static IniLine NewKeyValue(string section, string key, string value)
        {
            return new IniLine { Raw = key + "=" + value, Kind = IniLineKind.KeyValue, Section = section, Key = key, Value = value };
        }

        private int TrimTrailingBlankLines(int index)
        {
            while (index > 0 && lines[index - 1].Kind == IniLineKind.Blank) index--;

            return index;
        }
    }
}
