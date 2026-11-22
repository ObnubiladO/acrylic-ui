using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AcrylicUI.Core
{
    public enum HostsLineKind
    {
        Blank,
        Comment,
        Rule,
        Include
    }

    /// <summary>
    /// One line of AcrylicHosts.txt.
    /// </summary>
    public sealed class HostsLine
    {
        /// <summary>The text of the line exactly as it is (or will be) written in the file.</summary>
        public string Raw = "";

        /// <summary>The line terminator found in the file, null for a line added by the UI.</summary>
        public string Terminator;

        public HostsLineKind Kind;

        /// <summary>True for a rule or an include switched off by a '#' placed right in front of it.</summary>
        public bool Disabled;

        /// <summary>For a rule: an IP address, FW or NX. For an include: the file name.</summary>
        public string Target = "";

        public string[] Hosts = NoHosts;

        /// <summary>The text following the '#' character, if any.</summary>
        public string Comment;

        /// <summary>What is wrong with the line, or null.</summary>
        public string Problem;

        private static readonly string[] NoHosts = new string[0];

        public const string Forward = "FW";
        public const string Block = "NX";

        public bool IsForward => Kind == HostsLineKind.Rule && Target == Forward;
        public bool IsBlock => Kind == HostsLineKind.Rule && Target == Block;
        public bool IsMapping => Kind == HostsLineKind.Rule && Target != Forward && Target != Block;

        /// <summary>
        /// Parses a line the way the Acrylic service does, additionally recognizing switched off entries.
        /// </summary>
        public static HostsLine Parse(string raw)
        {
            HostsLine line = ParseActive(raw);

            if (line.Kind == HostsLineKind.Comment)
            {
                string trimmed = raw.TrimStart(' ', '\t');

                // By convention a '#' immediately followed by an entry is an entry that has been switched off, while
                // a '#' followed by a space is an ordinary comment.

                if (trimmed.Length > 1 && trimmed[0] == '#' && trimmed[1] != ' ' && trimmed[1] != '\t' && trimmed[1] != '#')
                {
                    HostsLine inner = ParseActive(trimmed.Substring(1));

                    if ((inner.Kind == HostsLineKind.Rule || inner.Kind == HostsLineKind.Include) && inner.Problem == null)
                    {
                        inner.Raw = raw; inner.Disabled = true;

                        return inner;
                    }
                }
            }

            return line;
        }

        private static HostsLine ParseActive(string raw)
        {
            var line = new HostsLine { Raw = raw };

            if (raw.Length == 0) { line.Kind = HostsLineKind.Blank; return line; }

            if (raw[0] == '@')
            {
                line.Kind = HostsLineKind.Include;

                if (raw.Length >= 3 && raw[1] == ' ')
                {
                    line.Target = raw.Substring(2);

                    if (line.Target.Trim().Length == 0) line.Problem = "The name of the file to include is missing.";
                }
                else
                {
                    line.Problem = "An include must be written as '@', a space and then a file name.";
                }

                return line;
            }

            var tokens = new List<string>();

            int start = 0, index = 0;

            for (; index < raw.Length; index++)
            {
                char c = raw[index];

                if (c == ' ' || c == '\t')
                {
                    if (index > start) tokens.Add(raw.Substring(start, index - start));

                    start = index + 1;
                }
                else if (c == '#')
                {
                    break;
                }
            }

            if (index > start) tokens.Add(raw.Substring(start, index - start));

            if (index < raw.Length) line.Comment = raw.Substring(index + 1).Trim();

            if (tokens.Count == 0)
            {
                line.Kind = (line.Comment != null) ? HostsLineKind.Comment : HostsLineKind.Blank;

                return line;
            }

            line.Kind = HostsLineKind.Rule;
            line.Target = Share(tokens[0]);

            tokens.RemoveAt(0);

            line.Hosts = tokens.ToArray();
            line.Problem = FindProblem(line.Target, line.Hosts);

            return line;
        }

        private static string lastTarget = "";

        /// <summary>
        /// Large hosts files repeat the same target on line after line: such lines share one string.
        /// </summary>
        private static string Share(string target)
        {
            string last = lastTarget;

            if (target == last) return last;

            lastTarget = target;

            return target;
        }

        public static string ValidateTarget(string target)
        {
            if (target == Forward || target == Block) return null;

            if (ConfigSchema.IsIPv4(target) || ConfigSchema.IsIPv6(target)) return null;

            if (string.Equals(target, Forward, StringComparison.OrdinalIgnoreCase) || string.Equals(target, Block, StringComparison.OrdinalIgnoreCase)) return "\"" + target + "\" must be written in upper case.";

            return "\"" + target + "\" is not an IP address, FW or NX.";
        }

        public static string ValidateHost(string host)
        {
            if (host.Length == 0) return "The domain name is empty.";

            if (host.IndexOf('#') >= 0) return "\"" + host + "\" contains a '#', which starts a comment.";

            if (host[0] != '/' && !DomainNames.IsAscii(host))
            {
                // Acrylic compares the entries with the names found in the DNS requests, which are always ASCII.

                string ascii = DomainNames.ToAsciiHostsEntry(host);

                return "\"" + host + "\" has characters outside ASCII, so it can never match: DNS requests carry such names in their xn-- form." + (ascii != host ? " Write " + ascii + " instead." : "");
            }

            if (host[0] == '/')
            {
                if (host.Length == 1) return "The regular expression is empty.";

                try
                {
                    new Regex(host.Substring(1), RegexOptions.IgnoreCase);
                }
                catch (ArgumentException e)
                {
                    return "The regular expression " + host + " is not valid: " + e.Message;
                }
            }
            else if (host[0] == '>' && host.Length == 1)
            {
                return "A domain name must follow the '>' character.";
            }

            return null;
        }

        private static string FindProblem(string target, string[] hosts)
        {
            string problem = ValidateTarget(target);

            if (problem != null) return problem;

            if (hosts.Length == 0) return "No domain name follows \"" + target + "\", so the line has no effect.";

            foreach (string host in hosts)
            {
                problem = ValidateHost(host);

                if (problem != null) return problem;
            }

            return null;
        }

        public static HostsLine CreateRule(string target, IEnumerable<string> hosts, string comment, bool disabled)
        {
            var builder = new StringBuilder();

            if (disabled) builder.Append('#');

            builder.Append(target);

            foreach (string host in hosts) builder.Append(' ').Append(host);

            if (!string.IsNullOrWhiteSpace(comment)) builder.Append(" # ").Append(comment.Trim());

            return Parse(builder.ToString());
        }

        public static HostsLine CreateInclude(string fileName, bool disabled)
        {
            return Parse((disabled ? "#" : "") + "@ " + fileName.Trim());
        }

        public static HostsLine CreateComment(string text)
        {
            return Parse(string.IsNullOrEmpty(text) ? "#" : "# " + text);
        }

        /// <summary>
        /// Returns the same entry switched on or off, keeping its text untouched.
        /// </summary>
        public HostsLine WithDisabled(bool disabled)
        {
            if (Kind != HostsLineKind.Rule && Kind != HostsLineKind.Include) return this;

            if (disabled == Disabled) return this;

            string trimmed = Raw.TrimStart(' ', '\t');

            HostsLine result = Parse(disabled ? "#" + trimmed : trimmed.Substring(1));

            result.Terminator = Terminator;

            return result;
        }
    }

    /// <summary>
    /// A line-preserving model of AcrylicHosts.txt: lines the user does not touch are written back verbatim.
    /// </summary>
    public sealed class HostsDocument
    {
        public List<HostsLine> Lines { get; } = new List<HostsLine>();

        private string newLine = "\r\n";
        private bool endsWithNewLine = true;

        public static HostsDocument Parse(string text)
        {
            var document = new HostsDocument();

            text = text ?? "";

            document.newLine = TextFile.DetectNewLine(text);
            document.endsWithNewLine = text.Length == 0 || text.EndsWith("\n", StringComparison.Ordinal);

            foreach (KeyValuePair<string, string> rawLine in TextFile.SplitLines(text))
            {
                HostsLine line = HostsLine.Parse(rawLine.Key);

                line.Terminator = rawLine.Value;

                document.Lines.Add(line);
            }

            return document;
        }

        public string ToText()
        {
            var builder = new StringBuilder();

            for (int i = 0; i < Lines.Count; i++)
            {
                builder.Append(Lines[i].Raw);
                builder.Append(TextFile.TerminatorFor(Lines[i].Terminator, i == Lines.Count - 1, newLine, endsWithNewLine));
            }

            return builder.ToString();
        }
    }
}
