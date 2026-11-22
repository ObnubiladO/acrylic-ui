using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AcrylicUI.Core
{
    public enum SettingKind
    {
        Text,
        IPv4Address,
        IPv6Address,
        IPAddress,
        Port,
        Integer,
        YesNo,
        Choice,
        DomainNameMask,
        QueryTypeMask,
        HitLogFilter
    }

    /// <summary>
    /// Describes one parameter of the [GlobalSection] of AcrylicConfiguration.ini.
    /// </summary>
    public sealed class SettingDefinition
    {
        public string Key;
        public string Label;
        public string Description;
        public SettingKind Kind;
        public string Category;

        /// <summary>The value found in the configuration file Acrylic is distributed with.</summary>
        public string Default = "";

        /// <summary>The value the Acrylic service assumes when the parameter is missing from the file.</summary>
        public string BuiltIn = "";

        public string Unit;
        public string[] Choices;
        public bool AllowEmpty = true;

        /// <summary>Index of the DNS server the parameter belongs to, or -1.</summary>
        public int ServerIndex = -1;

        /// <summary>For DNS server parameters: the protocol that makes the parameter relevant, or null.</summary>
        public string RelevantProtocol;
    }

    public static class ConfigSchema
    {
        public const string GlobalSection = "GlobalSection";
        public const string AllowedAddressesSection = "AllowedAddressesSection";

        public const string CategoryServers = "DNS servers";
        public const string CategoryCache = "Address cache";
        public const string CategoryRequests = "Request handling";
        public const string CategoryNetwork = "Local binding";
        public const string CategoryTimeouts = "Timeouts";
        public const string CategoryHitLog = "Hit log";
        public const string CategoryAllowed = "Allowed addresses";
        public const string CategoryOther = "Other parameters";

        public static readonly string[] ServerOrdinals = { "Primary", "Secondary", "Tertiary", "Quaternary", "Quinary", "Senary", "Septenary", "Octonary", "Nonary", "Denary" };

        public static readonly string[] Protocols = { "UDP", "TCP", "SOCKS5", "DOH" };

        public static readonly string[] QueryTypeNames = { "A", "AAAA", "ANY", "CNAME", "HTTPS", "MX", "NS", "PTR", "SOA", "SRV", "TXT" };

        public static readonly KeyValuePair<char, string>[] HitLogCodes =
        {
            new KeyValuePair<char, string>('X', "Resolved directly by Acrylic"),
            new KeyValuePair<char, string>('H', "Resolved using the hosts cache"),
            new KeyValuePair<char, string>('C', "Resolved using the address cache"),
            new KeyValuePair<char, string>('F', "Forwarded to your DNS servers"),
            new KeyValuePair<char, string>('R', "Response accepted from a DNS server"),
            new KeyValuePair<char, string>('U', "Silent update accepted from a DNS server")
        };

        private static List<SettingDefinition> definitions;
        private static Dictionary<string, SettingDefinition> definitionsByKey;

        public static IReadOnlyList<SettingDefinition> Definitions
        {
            get { EnsureBuilt(); return definitions; }
        }

        public static SettingDefinition Find(string key)
        {
            EnsureBuilt();

            return definitionsByKey.TryGetValue(key, out SettingDefinition definition) ? definition : null;
        }

        public static string ServerKey(int serverIndex, string suffix)
        {
            return ServerOrdinals[serverIndex] + "Server" + suffix;
        }

        public static string ServerIgnoreKey(int serverIndex, string what)
        {
            return "Ignore" + what + "ResponsesFrom" + ServerOrdinals[serverIndex] + "Server";
        }

        private static void EnsureBuilt()
        {
            if (definitions != null) return;

            var list = new List<SettingDefinition>();

            for (int i = 0; i < ServerOrdinals.Length; i++) AddServer(list, i);

            // Address cache

            list.Add(new SettingDefinition { Key = "AddressCacheDisabled", Category = CategoryCache, Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Disable the address cache", Description = "Turns the address cache off altogether: Acrylic then works as a forwarding-only DNS proxy." });
            list.Add(new SettingDefinition { Key = "AddressCacheInMemoryOnly", Category = CategoryCache, Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Keep the address cache in memory only", Description = "Disables any disk activity related to the address cache. The cache is then lost every time Acrylic stops, instead of being saved to AcrylicCache.dat." });
            list.Add(new SettingDefinition { Key = "AddressCacheScavengingTime", Category = CategoryCache, Kind = SettingKind.Integer, Unit = "minutes", Default = "5760", BuiltIn = "5760", Label = "Lifetime of a positive response", Description = "A cached response older than this is considered obsolete: the request is forwarded to your DNS servers and the client waits for the answer." });
            list.Add(new SettingDefinition { Key = "AddressCacheSilentUpdateTime", Category = CategoryCache, Kind = SettingKind.Integer, Unit = "minutes", Default = "1440", BuiltIn = "1440", Label = "Silent update after", Description = "A cached response older than this (but still within its lifetime) is sent to the client immediately, while Acrylic refreshes it from your DNS servers in the background." });
            list.Add(new SettingDefinition { Key = "AddressCacheNegativeTime", Category = CategoryCache, Kind = SettingKind.Integer, Unit = "minutes", Default = "60", BuiltIn = "60", Label = "Lifetime of a negative response", Description = "How long a \"this name does not exist\" response is kept in the address cache." });
            list.Add(new SettingDefinition { Key = "AddressCacheFailureTime", Category = CategoryCache, Kind = SettingKind.Integer, Unit = "minutes", Default = "0", BuiltIn = "0", Label = "Lifetime of a failure response", Description = "How long a failure response is kept in the address cache. With 0 failure responses are never cached." });
            list.Add(new SettingDefinition { Key = "AddressCachePeriodicPruningTime", Category = CategoryCache, Kind = SettingKind.Integer, Unit = "minutes", Default = "720", BuiltIn = "720", Label = "Prune obsolete items every", Description = "How often the address cache is cleaned of obsolete items. With 0 the address cache is never pruned." });
            list.Add(new SettingDefinition { Key = "AddressCacheDomainNameAffinityMask", Category = CategoryCache, Kind = SettingKind.DomainNameMask, Default = "^dns.msftncsi.com;^ipv6.msftconnecttest.com;^ipv6.msftncsi.com;^www.msftconnecttest.com;^www.msftncsi.com;*", Label = "Domain names to cache", Description = "A semicolon separated list of names or wildcards restricting which domain names are cached. Prepend a caret (^) to exclude. The first rule that matches decides; if no rule matches the response is not cached, so end the list with * to cache everything else. Leave empty to cache every domain name." });
            list.Add(new SettingDefinition { Key = "AddressCacheQueryTypeAffinityMask", Category = CategoryCache, Kind = SettingKind.QueryTypeMask, Default = "A;AAAA;CNAME;HTTPS;MX;NS;PTR;SOA;SRV;TXT", Label = "Query types to cache", Description = "A semicolon separated list of the query types whose responses are cached: A, AAAA, ANY, CNAME, HTTPS, MX, NS, PTR, SOA, SRV, TXT or any other type by its decimal value. Leave empty to cache every query type." });

            // Request handling

            list.Add(new SettingDefinition { Key = "SinkholeIPv6Lookups", Category = CategoryRequests, Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Sinkhole IPv6 lookups", Description = "Answers every IPv6 lookup (DNS requests of AAAA type) with an empty response instead of resolving it." });
            list.Add(new SettingDefinition { Key = "ForwardPrivateReverseLookups", Category = CategoryRequests, Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Forward private reverse lookups", Description = "Forwards reverse lookups (DNS requests of PTR type) for private IP ranges to your DNS servers. Leaving this off spares your DNS servers these usually needless queries and avoids leaking information about your private address space." });
            list.Add(new SettingDefinition { Key = "GeneratedResponseTimeToLive", Category = CategoryRequests, Kind = SettingKind.Integer, Unit = "seconds", Default = "300", BuiltIn = "60", Label = "Time to live of generated responses", Description = "The time to live set for the DNS responses Acrylic generates by itself, such as the ones coming from the mappings of the AcrylicHosts.txt file." });

            // Local binding

            list.Add(new SettingDefinition { Key = "LocalIPv4BindingAddress", Category = CategoryNetwork, Kind = SettingKind.IPv4Address, Default = "0.0.0.0", Label = "IPv4 address", Description = "The local IPv4 address Acrylic binds to. With 0.0.0.0 Acrylic receives DNS requests from all of your network interfaces, with the address of one interface only from that one. Leave empty to disable IPv4." });
            list.Add(new SettingDefinition { Key = "LocalIPv4BindingPort", Category = CategoryNetwork, Kind = SettingKind.Port, Default = "53", BuiltIn = "53", Label = "IPv4 port", Description = "The local UDP and TCP port Acrylic binds to on IPv4. 53 is the standard port for DNS resolution: change it only if you are using a non standard DNS client." });
            list.Add(new SettingDefinition { Key = "LocalIPv6BindingAddress", Category = CategoryNetwork, Kind = SettingKind.IPv6Address, Default = "0:0:0:0:0:0:0:0", Label = "IPv6 address", Description = "The local IPv6 address Acrylic binds to. With 0:0:0:0:0:0:0:0 Acrylic receives DNS requests from all of your network interfaces, with the address of one interface only from that one. Leave empty to disable IPv6." });
            list.Add(new SettingDefinition { Key = "LocalIPv6BindingPort", Category = CategoryNetwork, Kind = SettingKind.Port, Default = "53", BuiltIn = "53", Label = "IPv6 port", Description = "The local UDP and TCP port Acrylic binds to on IPv6. 53 is the standard port for DNS resolution: change it only if you are using a non standard DNS client." });
            list.Add(new SettingDefinition { Key = "LocalIPv6BindingEnabledOnWindowsVersionsPriorToWindowsVistaOrWindowsServer2008", Category = CategoryNetwork, Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Enable IPv6 binding before Windows Vista / Server 2008", Description = "On Windows versions prior to Windows Vista or Windows Server 2008 the IPv6 protocol is usually not installed by default. Turn this on only after having installed all the necessary prerequisites." });

            // Timeouts

            list.Add(Timeout("ServerUdpProtocolResponseTimeout", "UDP response timeout", "The maximum time to wait for a response coming from a DNS server configured with the UDP protocol."));
            list.Add(Timeout("ServerTcpProtocolResponseTimeout", "TCP response timeout", "The maximum time to wait for the first byte of a response coming from a DNS server configured with the TCP protocol."));
            list.Add(Timeout("ServerTcpProtocolInternalTimeout", "TCP internal timeout", "The maximum time to wait for the other bytes of a response coming from a DNS server configured with the TCP protocol."));
            list.Add(Timeout("ServerSocks5ProtocolProxyFirstByteTimeout", "SOCKS 5 proxy first byte timeout", "The maximum time to wait for the first byte coming from the intermediary SOCKS 5 proxy server."));
            list.Add(Timeout("ServerSocks5ProtocolProxyOtherBytesTimeout", "SOCKS 5 proxy other bytes timeout", "The maximum time to wait for the other bytes coming from the intermediary SOCKS 5 proxy server."));
            list.Add(Timeout("ServerSocks5ProtocolProxyRemoteConnectTimeout", "SOCKS 5 proxy remote connect timeout", "The maximum time to wait for the intermediary SOCKS 5 proxy server to connect to the DNS server."));
            list.Add(Timeout("ServerSocks5ProtocolProxyRemoteResponseTimeout", "SOCKS 5 proxy remote response timeout", "The maximum time to wait for the response of the DNS server through the intermediary SOCKS 5 proxy server."));

            // Hit log

            list.Add(new SettingDefinition { Key = "HitLogFileName", Category = CategoryHitLog, Kind = SettingKind.Text, Default = "", Label = "Hit log file name", Description = "The text file every DNS request and DNS response received by Acrylic is logged into. Leave empty to disable the hit log. The path can be absolute or relative and may contain the templates %DATE% (the current date as YYYYMMDD, which gives a daily log rotation), %TEMP%, %APPDATA% and %LOCALAPPDATA%. Example: HitLog.%DATE%.txt" });
            list.Add(new SettingDefinition { Key = "HitLogFileWhat", Category = CategoryHitLog, Kind = SettingKind.HitLogFilter, Default = "XHCF", Label = "What gets logged", Description = "The kinds of events written into the hit log." });
            list.Add(new SettingDefinition { Key = "HitLogFullDump", Category = CategoryHitLog, Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Full dump", Description = "Writes the full dump of DNS requests and responses into the hit log, in addition to their dissection." });
            list.Add(new SettingDefinition { Key = "HitLogMaxPendingHits", Category = CategoryHitLog, Kind = SettingKind.Integer, Unit = "items", Default = "512", BuiltIn = "256", Label = "Pending hits kept in memory", Description = "The maximum number of hit log items kept in memory before they are flushed to disk. The hit log is written only when this buffer is full or when Acrylic stops, so a line may appear in the file with some delay." });

            definitions = list;
            definitionsByKey = list.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        }

        private static SettingDefinition Timeout(string key, string label, string description)
        {
            return new SettingDefinition { Key = key, Category = CategoryTimeouts, Kind = SettingKind.Integer, Unit = "milliseconds", Default = "3989", BuiltIn = "3989", Label = label, Description = description };
        }

        private static void AddServer(List<SettingDefinition> list, int index)
        {
            string ordinal = ServerOrdinals[index].ToLowerInvariant();

            string defaultAddress = index == 0 ? "8.8.8.8" : index == 1 ? "8.8.4.4" : "";

            void Add(SettingDefinition definition)
            {
                definition.Category = CategoryServers;
                definition.ServerIndex = index;

                list.Add(definition);
            }

            Add(new SettingDefinition { Key = ServerKey(index, "Address"), Kind = SettingKind.IPAddress, Default = defaultAddress, Label = "Address", Description = "The IP address of your " + ordinal + " DNS server, either an IPv4 address in quad-dotted notation or an IPv6 address in colon-separated groups. Leave empty if you don't want to use this server." });
            Add(new SettingDefinition { Key = ServerKey(index, "Port"), Kind = SettingKind.Port, Default = "53", Label = "Port", Description = "The TCP/UDP port the DNS server listens to. 53 is the standard port for DNS resolution, 443 the standard one for DNS-over-HTTPS." });
            Add(new SettingDefinition { Key = ServerKey(index, "Protocol"), Kind = SettingKind.Choice, Choices = Protocols, Default = "UDP", Label = "Protocol", Description = "UDP forwards DNS requests using UDP or TCP in accordance with RFC 5625. TCP uses TCP only and should be chosen only where UDP isn't possible. SOCKS5 goes through a SOCKS 5 proxy as an intermediary (only the NO AUTHENTICATION REQUIRED method is supported). DOH uses DNS-over-HTTPS, which prevents eavesdropping and manipulation of DNS data." });

            Add(new SettingDefinition { Key = ServerKey(index, "DoHProtocolHost"), Kind = SettingKind.Text, RelevantProtocol = "DOH", Label = "DoH host name", Description = "The host name of the DNS-over-HTTPS server, for example dns.google." });
            Add(new SettingDefinition { Key = ServerKey(index, "DoHProtocolPath"), Kind = SettingKind.Text, RelevantProtocol = "DOH", Label = "DoH query path", Description = "The query path of the DNS-over-HTTPS server, for example dns-query." });
            Add(new SettingDefinition { Key = ServerKey(index, "DoHProtocolConnectionType"), Kind = SettingKind.Choice, Choices = new[] { "System", "Direct" }, Default = "System", BuiltIn = "System", RelevantProtocol = "DOH", Label = "DoH connection type", Description = "System connects to the internet using your system proxy configuration, Direct connects without using a proxy." });
            Add(new SettingDefinition { Key = ServerKey(index, "DoHProtocolReuseConnections"), Kind = SettingKind.YesNo, Default = "Yes", BuiltIn = "Yes", RelevantProtocol = "DOH", Label = "DoH: reuse connections", Description = "Allows Acrylic to reuse existing TCP connections when sending requests to this server, which is an effective way to improve the performance of DNS-over-HTTPS." });
            Add(new SettingDefinition { Key = ServerKey(index, "DoHProtocolUseWinHttp"), Kind = SettingKind.YesNo, Default = "Yes", BuiltIn = "Yes", RelevantProtocol = "DOH", Label = "DoH: use WinHttp", Description = "Uses the WinHttp library instead of the WinINet one. WinHttp is more efficient and therefore preferable, but switching can help with some compatibility issues." });

            Add(new SettingDefinition { Key = ServerKey(index, "Socks5ProtocolProxyAddress"), Kind = SettingKind.IPAddress, RelevantProtocol = "SOCKS5", Label = "SOCKS 5 proxy address", Description = "The IP address of the SOCKS 5 proxy server to use as an intermediary to this DNS server." });
            Add(new SettingDefinition { Key = ServerKey(index, "Socks5ProtocolProxyPort"), Kind = SettingKind.Port, RelevantProtocol = "SOCKS5", Label = "SOCKS 5 proxy port", Description = "The TCP port the SOCKS 5 proxy server listens to." });

            Add(new SettingDefinition { Key = ServerKey(index, "DomainNameAffinityMask"), Kind = SettingKind.DomainNameMask, Label = "Domain name affinity mask", Description = "A semicolon separated list of names or wildcards restricting which domain names get forwarded to this server, for example *.com;*.org. Prepend a caret (^) to negate, for example ^*.com;^*.org;* forwards everything except .com and .org. The first rule that matches decides; if no rule matches the request is not forwarded. Leave empty to forward every domain name." });
            Add(new SettingDefinition { Key = ServerKey(index, "QueryTypeAffinityMask"), Kind = SettingKind.QueryTypeMask, Label = "Query type affinity mask", Description = "A semicolon separated list restricting which query types get forwarded to this server, for example A;AAAA;MX;SRV. Leave empty to forward every query type." });

            Add(new SettingDefinition { Key = ServerIgnoreKey(index, "Failure"), Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Ignore failure responses", Description = "Ignores the failure responses coming from this DNS server." });
            Add(new SettingDefinition { Key = ServerIgnoreKey(index, "Negative"), Kind = SettingKind.YesNo, Default = "No", BuiltIn = "No", Label = "Ignore negative responses", Description = "Ignores the negative responses coming from this DNS server." });
        }

        // ------------------------------------------------------------------------------------------------------------
        // Value helpers
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Interprets a boolean parameter the way the Acrylic service does.
        /// </summary>
        public static bool ParseYesNo(string value)
        {
            string upper = (value ?? "").ToUpperInvariant();

            return upper == "YES" || upper == "TRUE" || upper == "1";
        }

        public static bool IsIPv4(string text)
        {
            if (!Regex.IsMatch(text, @"^\d{1,3}(\.\d{1,3}){3}$")) return false;

            return text.Split('.').All(part => int.Parse(part, CultureInfo.InvariantCulture) <= 255);
        }

        public static bool IsIPv6(string text)
        {
            if (text.IndexOf(':') < 0 || text.IndexOf('.') >= 0 || text.IndexOf('%') >= 0 || text.IndexOf('[') >= 0) return false;

            return IPAddress.TryParse(text, out IPAddress address) && address.AddressFamily == AddressFamily.InterNetworkV6;
        }

        /// <summary>
        /// Returns a description of what is wrong with the value, or null if the value is fine.
        /// </summary>
        public static string Validate(SettingDefinition definition, string value)
        {
            value = (value ?? "").Trim();

            if (value.Length == 0) return null;

            switch (definition.Kind)
            {
                case SettingKind.IPv4Address:
                    return IsIPv4(value) ? null : "Enter an IPv4 address in quad-dotted notation, like 192.168.1.1.";

                case SettingKind.IPv6Address:
                    return IsIPv6(value) ? null : "Enter an IPv6 address in colon-separated groups, like 2001:4860:4860::8888.";

                case SettingKind.IPAddress:
                    return IsIPv4(value) || IsIPv6(value) ? null : "Enter an IPv4 address like 8.8.8.8 or an IPv6 address like 2001:4860:4860::8888.";

                case SettingKind.Port:
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port >= 1 && port <= 65535 ? null : "Enter a port number between 1 and 65535.";

                case SettingKind.Integer:
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? null : "Enter a whole number, 0 or greater.";

                case SettingKind.Choice:
                    return definition.Choices.Any(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)) ? null : "Choose one of: " + string.Join(", ", definition.Choices) + ".";

                case SettingKind.QueryTypeMask:
                    foreach (string token in value.Split(';'))
                    {
                        string t = token.Trim();

                        if (t.Length == 0) continue;

                        if (Array.IndexOf(QueryTypeNames, t) >= 0) continue;

                        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int type) && type >= 1 && type <= 65535) continue;

                        if (Array.IndexOf(QueryTypeNames, t.ToUpperInvariant()) >= 0) return "Query type names must be written in upper case: use " + t.ToUpperInvariant() + " instead of " + t + ".";

                        return "\"" + t + "\" is not a query type Acrylic knows by name. Use " + string.Join(", ", QueryTypeNames) + " or a decimal value.";
                    }
                    return null;

                case SettingKind.HitLogFilter:
                    foreach (char c in value)
                    {
                        if (!HitLogCodes.Any(code => code.Key == c)) return "Only the status codes X, H, C, F, R and U can be used.";
                    }
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Turns a number of minutes, seconds or milliseconds into something easier to grasp, like "4 days".
        /// </summary>
        public static string Humanize(string value, string unit)
        {
            if (!long.TryParse((value ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long amount)) return "";

            double seconds;

            switch (unit)
            {
                case "minutes": seconds = amount * 60.0; if (amount < 60) return ""; break;
                case "seconds": seconds = amount; if (amount < 60) return ""; break;
                case "milliseconds": seconds = amount / 1000.0; if (amount < 1000) return ""; break;
                default: return "";
            }

            if (seconds < 60) return "= " + Round(seconds) + " s";
            if (seconds < 3600) return "= " + Round(seconds / 60) + " min";
            if (seconds < 86400) return "= " + Plural(seconds / 3600, "hour");

            return "= " + Plural(seconds / 86400, "day");
        }

        private static string Round(double value)
        {
            return Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string Plural(double value, string unit)
        {
            string text = Round(value);

            return text + " " + unit + (text == "1" ? "" : "s");
        }
    }
}
