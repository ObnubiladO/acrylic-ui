using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AcrylicUI.Core
{
    /// <summary>
    /// A port of the wildcard matching used by the Acrylic service (see PatternMatching.pas).
    /// </summary>
    public static class PatternMatching
    {
        public static bool Match(string element, string pattern)
        {
            return Match(element, 0, pattern, 0);
        }

        private static bool Match(string element, int e, string pattern, int p)
        {
            if (p == pattern.Length - 1 && pattern[p] == '*') return true;

            bool elementEnded = (e >= element.Length);
            bool patternEnded = (p >= pattern.Length);

            if (elementEnded) return patternEnded;

            if (patternEnded) return false;

            switch (pattern[p])
            {
                case '*': return Match(element, e, pattern, p + 1) || Match(element, e + 1, pattern, p);
                case '?': return Match(element, e + 1, pattern, p + 1);
                default: return UpCase(element[e]) == UpCase(pattern[p]) && Match(element, e + 1, pattern, p + 1);
            }
        }

        private static char UpCase(char c)
        {
            return (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
        }

        /// <summary>
        /// Evaluates a domain name affinity mask the way the Acrylic service does (see Configuration.pas).
        /// </summary>
        public static bool MatchAffinityMask(string domainName, string mask)
        {
            var rules = new List<string>();

            // Delphi's TStringList.DelimitedText, which the service uses, splits on spaces as well.

            foreach (string rule in (mask ?? "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) rules.Add(rule);

            if (rules.Count == 0) return true;

            foreach (string rule in rules)
            {
                if (rule[0] == '^')
                {
                    if (Match(domainName, rule.Substring(1))) return false;
                }
                else
                {
                    if (Match(domainName, rule)) return true;
                }
            }

            return false;
        }
    }

    public sealed class HostsMatch
    {
        /// <summary>FW, NX or the IP address.</summary>
        public string Target;

        /// <summary>The domain name, pattern or regular expression that matched, as written in the file.</summary>
        public string Entry;

        /// <summary>The included file the entry comes from, or null for AcrylicHosts.txt itself.</summary>
        public string SourceFile;

        /// <summary>Zero based index of the line within its file.</summary>
        public int LineIndex;
    }

    public sealed class HostsVerdict
    {
        public HostsMatch Forward;
        public HostsMatch Block;
        public HostsMatch IPv4;
        public HostsMatch IPv6;

        public bool Handled => Forward != null || Block != null || IPv4 != null || IPv6 != null;

        /// <summary>The entry deciding the answer to a lookup, following the order the service evaluates them in.</summary>
        public HostsMatch Deciding => Forward ?? Block ?? IPv4 ?? IPv6;

        public string Describe()
        {
            if (Forward != null) return "Forwarded to your DNS servers (FW entry)";

            if (Block != null) return "Blocked: answered with NXDOMAIN (NX entry)";

            if (IPv4 != null && IPv6 != null) return "Answered with " + IPv4.Target + " (A) and " + IPv6.Target + " (AAAA)";

            if (IPv4 != null) return "Answered with " + IPv4.Target + " (A); AAAA lookups get an empty answer";

            if (IPv6 != null) return "Answered with " + IPv6.Target + " (AAAA); A lookups get an empty answer";

            return "Not in the hosts file: resolved through the address cache and your DNS servers";
        }
    }

    /// <summary>
    /// Tells what the Acrylic service would do with a domain name given the content of the hosts file, mirroring the
    /// lookup order of HostsCache.pas: for each kind of entry exact names first, then patterns, then regular
    /// expressions, each in file order; FW entries win over NX entries, which win over address mappings.
    /// </summary>
    public sealed class HostsMatcher
    {
        private sealed class Table
        {
            public readonly Dictionary<string, HostsMatch> Exact = new Dictionary<string, HostsMatch>(StringComparer.Ordinal);
            public readonly List<KeyValuePair<string, HostsMatch>> Patterns = new List<KeyValuePair<string, HostsMatch>>();
            public readonly List<KeyValuePair<Regex, HostsMatch>> Expressions = new List<KeyValuePair<Regex, HostsMatch>>();

            public HostsMatch Find(string name)
            {
                if (Exact.TryGetValue(name.ToLowerInvariant(), out HostsMatch match)) return match;

                foreach (var pattern in Patterns)
                {
                    if (PatternMatching.Match(name, pattern.Key)) return pattern.Value;
                }

                foreach (var expression in Expressions)
                {
                    try
                    {
                        if (expression.Key.IsMatch(name)) return expression.Value;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                    }
                }

                return null;
            }
        }

        private readonly Table forward = new Table();
        private readonly Table block = new Table();
        private readonly Table ipv4 = new Table();
        private readonly Table ipv6 = new Table();

        /// <summary>Problems met while following the includes (missing files, recursion).</summary>
        public List<string> Warnings { get; } = new List<string>();

        public static HostsMatcher Build(HostsDocument document, string baseDirectory)
        {
            var matcher = new HostsMatcher();

            matcher.AddLines(document.Lines, null, baseDirectory, new List<string>());

            return matcher;
        }

        public static string MakeAbsolutePath(string baseDirectory, string path)
        {
            return path.IndexOf('\\') >= 0 ? path : Path.Combine(baseDirectory, path);
        }

        private void AddLines(IList<HostsLine> lines, string sourceFile, string baseDirectory, List<string> includeChain)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                HostsLine line = lines[i];

                if (line.Disabled) continue;

                if (line.Kind == HostsLineKind.Include)
                {
                    if (line.Problem == null) AddInclude(line.Target, baseDirectory, includeChain);

                    continue;
                }

                if (line.Kind != HostsLineKind.Rule || line.Hosts.Length == 0) continue;

                Table table = line.Target == HostsLine.Forward ? forward : line.Target == HostsLine.Block ? block : line.Target.IndexOf(':') >= 0 ? ipv6 : ipv4;

                foreach (string host in line.Hosts)
                {
                    AddHost(table, host, new HostsMatch { Target = line.Target, Entry = host, SourceFile = sourceFile, LineIndex = i });
                }
            }
        }

        private void AddInclude(string fileName, string baseDirectory, List<string> includeChain)
        {
            string path = MakeAbsolutePath(baseDirectory, fileName);

            string fullPath;

            try { fullPath = Path.GetFullPath(path); } catch (Exception) { Warnings.Add("The included file name \"" + fileName + "\" is not valid."); return; }

            if (!File.Exists(fullPath)) { Warnings.Add("The included file \"" + fileName + "\" does not exist, so Acrylic skips it."); return; }

            foreach (string ancestor in includeChain)
            {
                if (string.Equals(ancestor, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    Warnings.Add("The file \"" + fileName + "\" includes itself, directly or indirectly. This makes the Acrylic service fail on startup.");

                    return;
                }
            }

            HostsDocument included;

            try
            {
                included = HostsDocument.Parse(TextFile.Load(fullPath).Text);
            }
            catch (Exception e)
            {
                Warnings.Add("The included file \"" + fileName + "\" could not be read: " + e.Message);

                return;
            }

            includeChain.Add(fullPath);

            AddLines(included.Lines, Path.GetFileName(fullPath), baseDirectory, includeChain);

            includeChain.RemoveAt(includeChain.Count - 1);
        }

        private static void AddHost(Table table, string host, HostsMatch match)
        {
            if (host.Length == 0) return;

            if (host[0] == '/')
            {
                try
                {
                    table.Expressions.Add(new KeyValuePair<Regex, HostsMatch>(new Regex(host.Substring(1), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)), match));
                }
                catch (ArgumentException)
                {
                }
            }
            else if (host[0] == '>')
            {
                string name = host.Substring(1);

                table.Patterns.Add(new KeyValuePair<string, HostsMatch>("*." + name, match));

                if (HasWildcards(name)) table.Patterns.Add(new KeyValuePair<string, HostsMatch>(name, match)); else AddExact(table, name, match);
            }
            else if (HasWildcards(host))
            {
                table.Patterns.Add(new KeyValuePair<string, HostsMatch>(host, match));
            }
            else
            {
                AddExact(table, host, match);
            }
        }

        private static void AddExact(Table table, string name, HostsMatch match)
        {
            string key = name.ToLowerInvariant();

            // The first definition of a domain name wins, like in the service.

            if (!table.Exact.ContainsKey(key)) table.Exact.Add(key, match);
        }

        private static bool HasWildcards(string text)
        {
            return text.IndexOf('*') >= 0 || text.IndexOf('?') >= 0;
        }

        public HostsVerdict Resolve(string domainName)
        {
            domainName = (domainName ?? "").Trim().TrimEnd('.');

            return new HostsVerdict
            {
                Forward = forward.Find(domainName),
                Block = block.Find(domainName),
                IPv4 = ipv4.Find(domainName),
                IPv6 = ipv6.Find(domainName)
            };
        }
    }
}
