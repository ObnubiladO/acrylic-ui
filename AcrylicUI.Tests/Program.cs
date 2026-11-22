using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AcrylicUI.Core;

namespace AcrylicUI.Tests
{
    internal static class Program
    {
        private static int passed, failed;

        // The directory of the AcrylicUI project, and the one holding the files Acrylic is distributed with.

        private static string uiDirectory, sourcesDirectory;

        // True when the tests run inside the Acrylic source tree, false when the UI is on its own.

        private static bool insideSourceTree;

        private static int Main(string[] args)
        {
            FindDirectories();

            Console.WriteLine(insideSourceTree ? "Running inside the Acrylic source tree." : "Running on the UI alone, with the files of the Data directory.");

            Run("IniDocument", TestIniDocument);
            Run("ConfigSchema", TestConfigSchema);
            Run("PatternMatching", TestPatternMatching);
            Run("HostsDocument", TestHostsDocument);
            Run("HostsMatcher", TestHostsMatcher);
            Run("DnsPacket", TestDnsPacket);
            Run("AddressCacheFile", TestAddressCacheFile);
            Run("TextFile", TestTextFile);
            Run("Themes", TestThemes);
            Run("DomainNames", TestDomainNames);
            Run("Layout", TestLayout);

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ALL " + passed + " CHECKS PASSED." : failed + " CHECKS FAILED, " + passed + " passed.");

            return failed == 0 ? 0 : 1;
        }

        private static void FindDirectories()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;

            while (directory != null && !File.Exists(Path.Combine(directory, "AcrylicUI", "AcrylicUI.csproj"))) directory = Path.GetDirectoryName(directory);

            if (directory == null) throw new DirectoryNotFoundException("The AcrylicUI project was not found.");

            uiDirectory = Path.Combine(directory, "AcrylicUI");

            // Inside the Acrylic source tree the files checked are the ones Acrylic is distributed with. On its own the
            // UI comes with a copy of the two it edits, which TestLayout keeps identical to those.

            insideSourceTree = File.Exists(Path.Combine(directory, "AcrylicConfiguration.Template.ini"));

            sourcesDirectory = insideSourceTree ? directory : Path.Combine(directory, "AcrylicUI.Tests", "Data");
        }

        /// <summary>
        /// Returns the names that exist among the files Acrylic is distributed with. The first one must exist.
        /// </summary>
        private static List<string> Existing(params string[] names)
        {
            Check(File.Exists(Path.Combine(sourcesDirectory, names[0])), names[0] + " is available to the tests");

            return names.Where(name => File.Exists(Path.Combine(sourcesDirectory, name))).ToList();
        }

        private static void TestLayout()
        {
            string csproj = File.ReadAllText(Path.Combine(uiDirectory, "AcrylicUI.csproj"));

            Check(!csproj.Contains("Include=\"..\\") && !csproj.Contains(">..\\"), "the project takes no file from outside its directory");
            Check(File.Exists(Path.Combine(uiDirectory, "Assets", "Acrylic.ico")), "the icon is part of the project");

            if (!insideSourceTree) return;

            // What the UI carries along for when it is on its own must not drift from the source tree.

            string tree = Path.GetDirectoryName(uiDirectory);

            foreach (string name in new[] { "AcrylicConfiguration.ini", "AcrylicHosts.txt" })
            {
                Check(File.ReadAllBytes(Path.Combine(tree, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(tree, "AcrylicUI.Tests", "Data", name))), "the copy of " + name + " in the Data directory is identical to the one Acrylic is distributed with");
            }

            Check(File.ReadAllBytes(Path.Combine(tree, "Form.ico")).SequenceEqual(File.ReadAllBytes(Path.Combine(uiDirectory, "Assets", "Acrylic.ico"))), "the icon of the project is the one of the source tree");

            string versionInfo = File.ReadAllText(Path.Combine(tree, "AcrylicVersionInfo.pas"));

            foreach (string property in new[] { "AcrylicVersionNumber", "AcrylicReleaseDate" })
            {
                string inTree = System.Text.RegularExpressions.Regex.Match(versionInfo, property + "\\s*=\\s*'([^']*)'").Groups[1].Value;
                string onItsOwn = System.Text.RegularExpressions.Regex.Match(csproj, "<" + property + " Condition=[^>]*>([^<]*)<").Groups[1].Value;

                Check(inTree.Length > 0, property + " is read from the source tree");
                Equal(inTree, onItsOwn, property + " written in the project file for when the UI is on its own");
            }
        }

        private static void Run(string name, Action test)
        {
            Console.WriteLine(name);

            try
            {
                test();
            }
            catch (Exception e)
            {
                failed++;

                Console.WriteLine("  FAILED with " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Check(bool condition, string what)
        {
            if (condition) { passed++; return; }

            failed++;

            Console.WriteLine("  FAILED: " + what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), what + " (expected \"" + expected + "\", got \"" + actual + "\")");
        }

        // ------------------------------------------------------------------------------------------------------------

        private static void TestIniDocument()
        {
            foreach (string name in Existing("AcrylicConfiguration.ini", "AcrylicConfiguration.Template.ini"))
            {
                string text = TextFile.Load(Path.Combine(sourcesDirectory, name)).Text;

                Check(IniDocument.Parse(text).ToText() == text, name + " survives a load and save unchanged");
            }

            Equal("a=1\nb=2", IniDocument.Parse("a=1\nb=2").ToText(), "a file without final line terminator stays that way");

            const string mixed = "[S]\r\na=1\nb=2\r\n\nc=3";

            Equal(mixed, IniDocument.Parse(mixed).ToText(), "mixed line terminators survive a load and save");

            IniDocument mixedDocument = IniDocument.Parse(mixed);

            mixedDocument.Set("S", "a", "9"); mixedDocument.Set("S", "d", "4");

            Equal("[S]\r\na=9\nb=2\r\n\nc=3\r\nd=4", mixedDocument.ToText(), "edits leave the terminators of the other lines alone");

            const string sample = "; header\r\n[GlobalSection]\r\n; about A\r\nKeyA = 1 \r\n  KeyB=two words\r\nEmpty=\r\n\r\n[AllowedAddressesSection]\r\n; note\r\nIP1=10.*\r\nIP2=192.168.1.*\r\n\r\n[GlobalSection]\r\nKeyA=shadowed\r\n";

            IniDocument document = IniDocument.Parse(sample);

            Equal("1", document.Get("GlobalSection", "KeyA"), "values are trimmed and the first section wins");
            Equal("1", document.Get("globalsection", "keya"), "names are case-insensitive");
            Equal("two words", document.Get("GlobalSection", "KeyB"), "indented keys are read");
            Equal("", document.Get("GlobalSection", "Empty"), "an empty value is an empty string");
            Equal(null, document.Get("GlobalSection", "Missing"), "a missing key is null");

            document.Set("GlobalSection", "KeyA", "1");

            Check(document.ToText() == sample, "setting the same value changes nothing");

            document.Set("GlobalSection", "KeyB", "three");

            Equal(sample.Replace("  KeyB=two words", "  KeyB=three"), document.ToText(), "setting a value rewrites only what follows the equals sign");

            document.Set("GlobalSection", "KeyA", "9");

            Check(document.ToText().Contains("KeyA =9\r\n") && document.ToText().Contains("KeyA=shadowed"), "the first occurrence is the one that gets written");

            document.Set("GlobalSection", "NewKey", "x");

            Check(document.ToText().Contains("Empty=\r\nNewKey=x\r\n\r\n[AllowedAddressesSection]"), "a new key is appended to its section, before the blank lines");

            document.ReplaceEntries("AllowedAddressesSection", new[] { new KeyValuePair<string, string>("IP1", "*") });

            Check(document.ToText().Contains("[AllowedAddressesSection]\r\n; note\r\nIP1=*\r\n\r\n[GlobalSection]"), "replacing the entries of a section keeps its comments");
            Equal(1, document.GetEntries("AllowedAddressesSection").Count, "the entries of a section are listed");

            document.ReplaceEntries("AllowedAddressesSection", new KeyValuePair<string, string>[0]);

            Check(document.ToText().Contains("[AllowedAddressesSection]\r\n; note\r\n\r\n[GlobalSection]"), "a section can be emptied");

            IniDocument shipped = IniDocument.Parse(TextFile.Load(Path.Combine(sourcesDirectory, "AcrylicConfiguration.ini")).Text);

            shipped.ReplaceEntries("AllowedAddressesSection", new[] { new KeyValuePair<string, string>("IP1", "192.168.1.*"), new KeyValuePair<string, string>("IP2", "10.0.0.5") });

            // The shipped file ends with the section header, without a final line terminator.

            Check(shipped.ToText().EndsWith(";\r\n[AllowedAddressesSection]\r\nIP1=192.168.1.*\r\nIP2=10.0.0.5"), "entries are added to the empty section the shipped file ends with");
        }

        private static void TestConfigSchema()
        {
            IniDocument shipped = IniDocument.Parse(TextFile.Load(Path.Combine(sourcesDirectory, "AcrylicConfiguration.ini")).Text);

            var keysInFile = shipped.GetEntries(ConfigSchema.GlobalSection);

            foreach (var entry in keysInFile)
            {
                SettingDefinition definition = ConfigSchema.Find(entry.Key);

                Check(definition != null, "the parameter " + entry.Key + " of the shipped configuration file is known to the UI");

                if (definition != null)
                {
                    Equal(entry.Value, definition.Default, "default of " + entry.Key);
                    Equal(null, ConfigSchema.Validate(definition, entry.Value), "the shipped value of " + entry.Key + " is valid");
                }
            }

            foreach (SettingDefinition definition in ConfigSchema.Definitions)
            {
                Check(shipped.Contains(ConfigSchema.GlobalSection, definition.Key), "the parameter " + definition.Key + " known to the UI exists in the shipped configuration file");
                Check(!string.IsNullOrEmpty(definition.Label) && !string.IsNullOrEmpty(definition.Description) && definition.Category != null, definition.Key + " is fully described");
            }

            Equal(keysInFile.Count, ConfigSchema.Definitions.Count, "number of parameters");

            Check(ConfigSchema.ParseYesNo("Yes") && ConfigSchema.ParseYesNo("yes") && ConfigSchema.ParseYesNo("TRUE") && ConfigSchema.ParseYesNo("1"), "what the service takes as true");
            Check(!ConfigSchema.ParseYesNo("No") && !ConfigSchema.ParseYesNo("") && !ConfigSchema.ParseYesNo("on"), "what the service takes as false");

            Check(ConfigSchema.IsIPv4("8.8.8.8") && !ConfigSchema.IsIPv4("8.8.8") && !ConfigSchema.IsIPv4("256.1.1.1") && !ConfigSchema.IsIPv4("a.b.c.d"), "IPv4 addresses");
            Check(ConfigSchema.IsIPv6("::1") && ConfigSchema.IsIPv6("0:0:0:0:0:0:0:0") && ConfigSchema.IsIPv6("2001:4860:4860::8888") && !ConfigSchema.IsIPv6("8.8.8.8") && !ConfigSchema.IsIPv6("fe80::1%eth0") && !ConfigSchema.IsIPv6("12345::"), "IPv6 addresses");

            SettingDefinition port = ConfigSchema.Find("PrimaryServerPort");

            Check(ConfigSchema.Validate(port, "53") == null && ConfigSchema.Validate(port, "") == null && ConfigSchema.Validate(port, "0") != null && ConfigSchema.Validate(port, "65536") != null && ConfigSchema.Validate(port, "dns") != null, "ports");

            SettingDefinition mask = ConfigSchema.Find("AddressCacheQueryTypeAffinityMask");

            Check(ConfigSchema.Validate(mask, "A;AAAA;65") == null && ConfigSchema.Validate(mask, "A;aaaa") != null && ConfigSchema.Validate(mask, "A;BOGUS") != null, "query type masks");

            SettingDefinition what = ConfigSchema.Find("HitLogFileWhat");

            Check(ConfigSchema.Validate(what, "XHCFRU") == null && ConfigSchema.Validate(what, "XZ") != null, "hit log filters");

            Equal("= 4 days", ConfigSchema.Humanize("5760", "minutes"), "humanized minutes");
            Equal("= 1 day", ConfigSchema.Humanize("1440", "minutes"), "humanized day");
            Equal("= 5 min", ConfigSchema.Humanize("300", "seconds"), "humanized seconds");
            Equal("= 4 s", ConfigSchema.Humanize("3989", "milliseconds"), "humanized milliseconds");
            Equal("", ConfigSchema.Humanize("30", "minutes"), "small amounts need no help");
        }

        private static void TestPatternMatching()
        {
            Check(PatternMatching.Match("anything", "*"), "* matches everything");
            Check(PatternMatching.Match("", "*"), "* matches the empty name");
            Check(PatternMatching.Match("ads.example.com", "ads.*"), "trailing *");
            Check(PatternMatching.Match("www.example.com", "*.example.com"), "leading *");
            Check(!PatternMatching.Match("example.com", "*.example.com"), "*.name does not match the bare name");
            Check(PatternMatching.Match("ad1.example.com", "ad?.example.com"), "? matches one character");
            Check(!PatternMatching.Match("ad.example.com", "ad?.example.com"), "? needs one character");
            Check(PatternMatching.Match("WWW.Example.COM", "www.example.com"), "matching ignores case");
            Check(!PatternMatching.Match("www.example.com", "www.example"), "the whole name must match");
            Check(!PatternMatching.Match("www.example", "www.example.com"), "the whole pattern must match");
            Check(PatternMatching.Match("a.b.c", "a*c"), "* in the middle");

            Check(PatternMatching.MatchAffinityMask("www.google.com", ""), "an empty affinity mask matches everything");
            Check(PatternMatching.MatchAffinityMask("www.google.com", "*.com"), "positive affinity rule");
            Check(!PatternMatching.MatchAffinityMask("www.google.net", "*.com;*.org"), "no affinity rule matches");
            Check(!PatternMatching.MatchAffinityMask("www.google.com", "^*.com;^*.org;*"), "negated affinity rule");
            Check(PatternMatching.MatchAffinityMask("www.google.net", "^*.com;^*.org;*"), "affinity catch-all");
            Check(!PatternMatching.MatchAffinityMask("dns.msftncsi.com", ConfigSchema.Find("AddressCacheDomainNameAffinityMask").Default), "the default cache mask excludes the connectivity probes");
        }

        private static void TestHostsDocument()
        {
            foreach (string name in Existing("AcrylicHosts.txt", "AcrylicHosts.Template.txt"))
            {
                string text = TextFile.Load(Path.Combine(sourcesDirectory, name)).Text;

                Check(HostsDocument.Parse(text).ToText() == text, name + " survives a load and save unchanged");
            }

            HostsDocument shipped = HostsDocument.Parse(TextFile.Load(Path.Combine(sourcesDirectory, "AcrylicHosts.txt")).Text);

            Equal(2, shipped.Lines.Count(l => l.Kind == HostsLineKind.Rule), "the shipped hosts file holds two rules");
            Equal(0, shipped.Lines.Count(l => l.Disabled), "the examples in the comments of the shipped hosts file are not taken for switched off rules");
            Equal(0, shipped.Lines.Count(l => l.Problem != null), "the shipped hosts file has no problems");

            Equal("a\nb", HostsDocument.Parse("a\nb").ToText(), "a file without final line terminator stays that way");
            Equal("# a\r\n1.1.1.1 x\n\n# b\r\n", HostsDocument.Parse("# a\r\n1.1.1.1 x\n\n# b\r\n").ToText(), "mixed line terminators survive a load and save");

            HostsDocument appended = HostsDocument.Parse("# a\r\n1.1.1.1 x");

            appended.Lines.Add(HostsLine.Parse("NX y"));

            Equal("# a\r\n1.1.1.1 x\r\nNX y", appended.ToText(), "a line added after an unterminated last line starts on its own line");
            Equal("", HostsDocument.Parse("").ToText(), "an empty file stays empty");
            Equal(0, HostsDocument.Parse("").Lines.Count, "an empty file has no lines");

            HostsLine line = HostsLine.Parse("127.0.0.1 localhost  localhost.localdomain\t# loopback ");

            Equal(HostsLineKind.Rule, line.Kind, "rule kind");
            Equal("127.0.0.1", line.Target, "rule target");
            Equal("localhost|localhost.localdomain", string.Join("|", line.Hosts), "rule hosts");
            Equal("loopback", line.Comment, "rule comment");
            Check(line.Problem == null && !line.Disabled && line.IsMapping, "rule state");

            line = HostsLine.Parse("  NX ads.example.com#tail");

            Check(line.IsBlock && line.Hosts.Length == 1 && line.Hosts[0] == "ads.example.com" && line.Comment == "tail", "leading spaces are fine and '#' ends a domain name");

            Check(HostsLine.Parse("FW >example.com /^a\\d+\\.").IsForward, "forward rule");
            Check(HostsLine.Parse("fw example.com").Problem != null, "FW must be upper case");
            Check(HostsLine.Parse("example.com 127.0.0.1").Problem != null, "a swapped line is reported");
            Check(HostsLine.Parse("127.0.0.1").Problem != null, "a rule without domain names is reported");
            Check(HostsLine.Parse("NX /(unclosed").Problem != null, "a broken regular expression is reported");
            Check(HostsLine.Parse("::1 localhost").Problem == null && HostsLine.Parse("2001:db8::1 host").IsMapping, "IPv6 targets");

            Equal(HostsLineKind.Comment, HostsLine.Parse("# 127.0.0.1 ad.* ads.*").Kind, "a commented example stays a comment");
            Equal(HostsLineKind.Comment, HostsLine.Parse("#").Kind, "a bare '#' is a comment");
            Equal(HostsLineKind.Comment, HostsLine.Parse("#hashtag text").Kind, "a comment without space stays a comment unless it is a valid entry");
            Equal(HostsLineKind.Blank, HostsLine.Parse("   \t").Kind, "whitespace only is a blank line");

            line = HostsLine.Parse("#0.0.0.0 tracker.example.com # why");

            Check(line.Kind == HostsLineKind.Rule && line.Disabled && line.Target == "0.0.0.0" && line.Comment == "why", "a '#' right in front of a rule is a switched off rule");

            HostsLine enabled = line.WithDisabled(false);

            Equal("0.0.0.0 tracker.example.com # why", enabled.Raw, "switching a rule on keeps its text");
            Equal(line.Raw, enabled.WithDisabled(true).Raw, "switching a rule off again");

            line = HostsLine.Parse("@ AcrylicHostsGroup1.txt");

            Check(line.Kind == HostsLineKind.Include && line.Target == "AcrylicHostsGroup1.txt" && line.Problem == null, "include");
            Check(HostsLine.Parse("@AcrylicHostsGroup1.txt").Problem != null, "an include needs the space");
            Check(HostsLine.Parse("#@ Group.txt").Disabled && HostsLine.Parse("#@ Group.txt").Kind == HostsLineKind.Include, "switched off include");
            Equal("@ Group.txt", HostsLine.Parse("#@ Group.txt").WithDisabled(false).Raw, "switching an include on");

            Equal("NX a.example b.example # ads", HostsLine.CreateRule("NX", new[] { "a.example", "b.example" }, " ads ", false).Raw, "composing a rule");
            Equal("#1.2.3.4 a.example", HostsLine.CreateRule("1.2.3.4", new[] { "a.example" }, null, true).Raw, "composing a switched off rule");
            Check(HostsLine.CreateRule("1.2.3.4", new[] { "a.example" }, null, true).Disabled, "a composed switched off rule reads back as such");
        }

        private static void TestHostsMatcher()
        {
            string directory = Path.Combine(Path.GetTempPath(), "AcrylicUI.Tests." + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(directory);

            try
            {
                File.WriteAllText(Path.Combine(directory, "Group.txt"), "NX included.example\r\n@ Group.txt\r\n");

                HostsDocument document = HostsDocument.Parse(string.Join("\r\n", new[]
                {
                    "127.0.0.1 localhost",
                    "::1 localhost",
                    "1.1.1.1 dup.example",
                    "2.2.2.2 dup.example",
                    "0.0.0.0 ad.* ad?.example.net",
                    "NX >blocked.example",
                    "NX /^track(er|ing)\\d*\\.",
                    "FW ok.blocked.example",
                    "#NX off.example",
                    "9.9.9.9 >site.example",
                    "5.5.5.5 only4.example",
                    "@ Group.txt",
                    "@ Missing.txt"
                }) + "\r\n");

                HostsMatcher matcher = HostsMatcher.Build(document, directory);

                HostsVerdict verdict = matcher.Resolve("LocalHost");

                Check(verdict.IPv4 != null && verdict.IPv4.Target == "127.0.0.1" && verdict.IPv6 != null && verdict.IPv6.Target == "::1", "exact names are matched without regard to case, per address family");
                Equal(0, verdict.IPv4.LineIndex, "the matching line is reported");

                Equal("1.1.1.1", matcher.Resolve("dup.example").IPv4.Target, "the first definition of a name wins");
                Check(matcher.Resolve("ad.server.com").IPv4 != null && matcher.Resolve("ad7.example.net").IPv4 != null && matcher.Resolve("adxx.example.net").IPv4 == null, "patterns");

                Check(matcher.Resolve("blocked.example").Block != null && matcher.Resolve("www.blocked.example").Block != null && matcher.Resolve("notblocked.example").Block == null, "'>' covers the name and its subdomains only");
                Check(matcher.Resolve("tracker12.site.com").Block != null && matcher.Resolve("TRACKING.site.com").Block != null && matcher.Resolve("mytracker.site.com").Block == null, "regular expressions, without regard to case");

                verdict = matcher.Resolve("ok.blocked.example");

                Check(verdict.Forward != null && verdict.Block != null && verdict.Deciding == verdict.Forward, "FW entries win over NX entries");
                Check(matcher.Resolve("www.site.example").Block == null && matcher.Resolve("site.example").IPv4.Target == "9.9.9.9", "'>' on an address mapping");
                Check(!matcher.Resolve("off.example").Handled, "switched off rules are ignored");
                Check(!matcher.Resolve("unknown.example").Handled, "unknown names are not handled");
                Check(matcher.Resolve("only4.example").Describe().Contains("AAAA lookups get an empty answer"), "an IPv4 only mapping empties the AAAA answer");

                verdict = matcher.Resolve("included.example");

                Check(verdict.Block != null && verdict.Block.SourceFile == "Group.txt", "included files are followed");
                Equal(2, matcher.Warnings.Count, "a missing include and a recursive include are reported");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        // ------------------------------------------------------------------------------------------------------------

        private static byte[] BuildResponse(string name, ushort type, int responseCode, params byte[][] answers)
        {
            var bytes = new List<byte>(DnsPacket.BuildQuery(0, name, type));

            bytes[2] = 0x81; bytes[3] = (byte)(0x80 | responseCode);
            bytes[7] = (byte)answers.Length;

            foreach (byte[] data in answers)
            {
                ushort answerType = data.Length == 4 ? DnsPacket.TypeA : data.Length == 16 ? DnsPacket.TypeAAAA : DnsPacket.TypeCNAME;

                bytes.AddRange(new byte[] { 0xC0, 0x0C, (byte)(answerType >> 8), (byte)answerType, 0, 1, 0, 0, 1, 44, (byte)(data.Length >> 8), (byte)data.Length });
                bytes.AddRange(data);
            }

            return bytes.ToArray();
        }

        private static byte[] BuildCacheRecord(byte fill, byte options, DateTime time, byte[] response)
        {
            var bytes = new List<byte>();

            for (int i = 0; i < 16; i++) bytes.Add(fill);

            bytes.Add(options);
            bytes.AddRange(BitConverter.GetBytes((int)(time - new DateTime(1980, 1, 1)).TotalMinutes));
            bytes.AddRange(BitConverter.GetBytes(response.Length));
            bytes.AddRange(response);

            return bytes.ToArray();
        }

        private static void TestDnsPacket()
        {
            byte[] query = DnsPacket.BuildQuery(0x1234, "www.example.com.", DnsPacket.TypeAAAA);

            Equal(12 + 17 + 4, query.Length, "query length");

            DnsMessage message = DnsPacket.TryParse(query, 0, query.Length);

            Check(message != null && message.Id == 0x1234 && message.QuestionName == "www.example.com" && message.QuestionType == DnsPacket.TypeAAAA, "a query reads back");

            byte[] response = BuildResponse("example.com", DnsPacket.TypeA, 0, new byte[] { 0xC0, 0x0C }, new byte[] { 93, 184, 215, 14 }, new byte[] { 10, 0, 0, 1 });

            message = DnsPacket.TryParse(response, 0, response.Length);

            Check(message != null && message.Answers.Count == 3, "answers are read");
            Equal("example.com", message.Answers[0].Text, "a compressed name is followed");
            Equal("93.184.215.14, 10.0.0.1", message.Summarize(), "the summary prefers the records of the type asked for");

            response = BuildResponse("nothere.example", DnsPacket.TypeA, 3);

            Equal("Name does not exist (NXDOMAIN)", DnsPacket.TryParse(response, 0, response.Length).Summarize(), "negative response summary");

            Check(DnsPacket.TryParse(new byte[5], 0, 5) == null, "a truncated header is rejected");
            Check(DnsPacket.TryParse(response, 0, 20) == null, "a truncated question is rejected");

            byte[] loop = { 0, 0, 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 0x0C, 0, 1, 0, 1 };

            Check(DnsPacket.TryParse(loop, 0, loop.Length) == null, "a compression loop is rejected");

            bool rejected = false;

            try { DnsPacket.BuildQuery(1, "bad..name", DnsPacket.TypeA); } catch (FormatException) { rejected = true; }

            Check(rejected, "an empty label is rejected");

            Equal((ushort)65, DnsPacket.ParseTypeName("https"), "type names");
        }

        private static void TestAddressCacheFile()
        {
            DateTime now = new DateTime(2026, 11, 22, 14, 50, 0);

            var content = new List<byte>();

            content.AddRange(BuildCacheRecord(1, 0, now, BuildResponse("example.com", DnsPacket.TypeA, 0, new byte[] { 93, 184, 215, 14 })));
            content.AddRange(BuildCacheRecord(2, 1, now, BuildResponse("nothere.example", DnsPacket.TypeA, 3)));
            content.AddRange(BuildCacheRecord(3, 0, now, BuildResponse("Example.COM", DnsPacket.TypeA, 0, new byte[] { 93, 184, 215, 14 })));
            content.AddRange(BuildCacheRecord(4, 0, now, BuildResponse("example.com", DnsPacket.TypeAAAA, 0, new byte[16])));
            content.AddRange(BuildCacheRecord(5, 0, now, BuildResponse("www.example.com", DnsPacket.TypeA, 0, new byte[] { 1, 2, 3, 4 })));
            content.AddRange(BuildCacheRecord(6, 0, now, BuildResponse("notexample.com", DnsPacket.TypeA, 0, new byte[] { 5, 6, 7, 8 })));
            content.AddRange(BuildCacheRecord(7, 2, now, new byte[] { 1, 2, 3 }));

            byte[] bytes = content.ToArray();

            AddressCacheFile file = AddressCacheFile.Parse(bytes, now);

            Check(file.IsWellFormed, "the cache is well formed");
            Equal(7, file.Entries.Count, "every record is read");
            Equal("example.com", file.Entries[0].DomainName, "domain name of an item");
            Equal(now, file.Entries[0].Time, "time of an item");
            Equal("93.184.215.14", file.Entries[0].Answer, "answer of an item");
            Equal(CacheResponseKind.Negative, file.Entries[1].Kind, "kind of a negative item");
            Equal(DnsPacket.TypeAAAA, file.Entries[3].QueryType, "query type of an item");
            Equal("", file.Entries[6].DomainName, "an unreadable response has no domain name");
            Equal(CacheResponseKind.Failure, file.Entries[6].Kind, "kind of a failure item");

            Check(!AddressCacheFile.Parse(bytes.Take(bytes.Length - 1).ToArray(), now).IsWellFormed, "a truncated file is detected");

            Check(AddressCacheFile.DomainMatches("Example.COM", "example.com", false), "domain names are compared without regard to case");
            Check(!AddressCacheFile.DomainMatches("www.example.com", "example.com", false) && AddressCacheFile.DomainMatches("www.example.com", "example.com", true), "subdomains are matched only on request");
            Check(!AddressCacheFile.DomainMatches("notexample.com", "example.com", true), "a name that merely ends the same way is not a subdomain");
            Check(!AddressCacheFile.DomainMatches("", "example.com", true), "unreadable items never match");

            Equal("example.com", AddressCacheFile.NormalizeDomainName("  https://user@Example.com:8443/path?x=1 "), "a pasted URL is reduced to its domain name");
            Equal("example.com", AddressCacheFile.NormalizeDomainName("EXAMPLE.com."), "a trailing dot is dropped");

            string path = Path.Combine(Path.GetTempPath(), "AcrylicUI.Tests." + Guid.NewGuid().ToString("N") + ".dat");

            try
            {
                File.WriteAllBytes(path, bytes);

                List<CacheEntry> removed = AddressCacheFile.Remove(path, entry => AddressCacheFile.DomainMatches(entry.DomainName, "example.com", false));

                Equal(3, removed.Count, "every item of the domain is removed, whatever its spelling or query type");

                AddressCacheFile after = AddressCacheFile.Load(path);

                Equal("nothere.example|www.example.com|notexample.com|", string.Join("|", after.Entries.Select(e => e.DomainName)), "the other items are kept, in order");

                byte[] expected = bytes.Skip(file.Entries[1].Offset).Take(file.Entries[1].RecordLength).Concat(bytes.Skip(file.Entries[4].Offset)).ToArray();

                Check(File.ReadAllBytes(path).SequenceEqual(expected), "the kept records are copied byte for byte");

                DateTime written = File.GetLastWriteTimeUtc(path);

                Equal(0, AddressCacheFile.Remove(path, entry => AddressCacheFile.DomainMatches(entry.DomainName, "absent.example", true)).Count, "removing an absent domain removes nothing");
                Equal(written, File.GetLastWriteTimeUtc(path), "and leaves the file alone");

                Equal(1, AddressCacheFile.Remove(path, entry => AddressCacheFile.DomainMatches(entry.DomainName, "example.com", true)).Count, "subdomains can be removed");

                File.WriteAllBytes(path, bytes.Take(bytes.Length - 2).ToArray());

                bool refused = false;

                try { AddressCacheFile.Remove(path, entry => true); } catch (InvalidDataException) { refused = true; }

                Check(refused && new FileInfo(path).Length == bytes.Length - 2, "a file with an unexpected layout is left untouched");

                File.Delete(path);

                Check(!AddressCacheFile.Load(path).Exists && AddressCacheFile.Remove(path, entry => true).Count == 0, "a missing cache file is an empty cache");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// The light and the dark appearance must define the same brushes, and every brush the application asks for
        /// must exist: a brush missing from one of the two would silently leave something unpainted in that one only.
        /// </summary>
        private static void TestThemes()
        {
            string directory = uiDirectory;

            var keyPattern = new System.Text.RegularExpressions.Regex("<SolidColorBrush x:Key=\"(\\w+)\"");

            List<string> light = keyPattern.Matches(File.ReadAllText(Path.Combine(directory, "Themes", "Light.xaml"))).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();
            List<string> dark = keyPattern.Matches(File.ReadAllText(Path.Combine(directory, "Themes", "Dark.xaml"))).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();

            Check(light.Count > 20, "the light palette is read");
            Equal(string.Join(" ", light), string.Join(" ", dark), "the two palettes define the same brushes, in the same order");
            Equal(light.Count, light.Distinct().Count(), "no brush is defined twice");

            var used = new HashSet<string>();

            foreach (string file in Directory.GetFiles(directory, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\")))
            {
                string text = File.ReadAllText(file);

                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, "\\{DynamicResource (\\w+)\\}")) used.Add(match.Groups[1].Value);

                if (!file.EndsWith("Light.xaml") && !file.EndsWith("Dark.xaml"))
                {
                    Check(!System.Text.RegularExpressions.Regex.IsMatch(text, "#[0-9A-Fa-f]{6,8}\"|\"White\"|\"Black\"|StaticResource \\w+Brush"), Path.GetFileName(file) + " names no colour directly");
                }
            }

            // The brushes asked for from code (see the Tones class).

            foreach (string tone in new[] { "Green", "Red", "Amber" }) { used.Add(tone + "Brush"); used.Add(tone + "SoftBrush"); }

            foreach (string key in new[] { "AccentTextBrush", "AccentSoftBrush", "GraySoftBrush", "SubtleBrush", "TextBrush" }) used.Add(key);

            foreach (string key in used.OrderBy(k => k)) Check(light.Contains(key), "the brush " + key + " used by the application exists in the palettes");

            foreach (string key in light) Check(used.Contains(key), "the brush " + key + " of the palettes is used");
        }

        private static void TestDomainNames()
        {
            const string accented = "m\u00FCnchen.de";
            const string ascii = "xn--mnchen-3ya.de";

            Equal(ascii, DomainNames.ToAscii(accented), "a name with an accent is converted to its xn-- form");
            Equal("www." + ascii, DomainNames.ToAscii("www." + accented), "only the labels that need it are converted");
            Equal("www.example.com", DomainNames.ToAscii("www.example.com"), "an ASCII name is left alone");
            Equal(accented, DomainNames.ToUnicode(ascii), "the xn-- form reads back");
            Equal("www.example.com", DomainNames.ToUnicode("www.example.com"), "a plain name has no other form");
            Equal("xn--zz-broken-.es", DomainNames.ToUnicode("xn--zz-broken-.es"), "a broken xn-- name is left alone");

            Equal(">" + ascii, DomainNames.ToAsciiHostsEntry(">" + accented), "the '>' shortcut of the hosts file is kept");
            Equal("*." + ascii, DomainNames.ToAsciiHostsEntry("*." + accented), "a wildcard label is kept, the others are converted");
            Equal("/producci\u00F3n", DomainNames.ToAsciiHostsEntry("/producci\u00F3n"), "regular expressions are left alone");

            Equal(ascii, AddressCacheFile.NormalizeDomainName("https://M\u00DCnchen.de/some/page"), "a pasted URL with an accented name is reduced to the xn-- form");

            Check(HostsLine.Parse("1.2.3.4 " + accented).Problem != null && HostsLine.Parse("1.2.3.4 " + accented).Problem.Contains(ascii), "an accented name in the hosts file is reported, with the form to write instead");
            Check(HostsLine.Parse("1.2.3.4 " + ascii).Problem == null, "its xn-- form is fine");

            HostsMatcher matcher = HostsMatcher.Build(HostsDocument.Parse("192.0.2.10 " + ascii + "\r\n"), Path.GetTempPath());

            Equal("192.0.2.10", matcher.Resolve(AddressCacheFile.NormalizeDomainName(accented)).IPv4?.Target, "trying the accented name finds the entry written in the xn-- form");

            Check(AddressCacheFile.DomainMatches(ascii, AddressCacheFile.NormalizeDomainName(accented), false), "clearing the accented name finds the cached xn-- one");
        }

        private static void TestTextFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "AcrylicUI.Tests." + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                foreach (byte[] original in new[]
                {
                    Encoding.ASCII.GetBytes("# plain\r\n127.0.0.1 localhost\r\n"),
                    new byte[] { 0x23, 0x20, 0xE9, 0xE8, 0x0D, 0x0A },                         // ANSI
                    new byte[] { 0x23, 0x20, 0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0x0A },             // UTF-8
                    new byte[] { 0xEF, 0xBB, 0xBF, 0x23, 0x20, 0xC3, 0xA9, 0x0D, 0x0A }        // UTF-8 with byte order mark
                })
                {
                    File.WriteAllBytes(path, original);

                    TextFile file = TextFile.Load(path);

                    file.Save(path);

                    Check(File.ReadAllBytes(path).SequenceEqual(original), "a file of " + original.Length + " bytes is saved back byte for byte");
                    Check(!File.Exists(path + ".tmp"), "no temporary file is left behind");
                }

                Equal("\n", TextFile.DetectNewLine("a\nb\n"), "unix line terminator");
                Equal("\r\n", TextFile.DetectNewLine("a\r\nb"), "windows line terminator");
                Equal("\r\n", TextFile.DetectNewLine("single line"), "default line terminator");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
