using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AcrylicUI.Core;

namespace AcrylicUI.Views
{
    /// <summary>
    /// One status code of the hit log filter, shown as a check box.
    /// </summary>
    public sealed class HitLogFlag : Observable
    {
        private readonly SettingRow owner;

        public char Code { get; }
        public string Label { get; }

        public HitLogFlag(SettingRow owner, char code, string label)
        {
            this.owner = owner; Code = code; Label = code + "  -  " + label;
        }

        public bool IsChecked
        {
            get { return owner.Value.IndexOf(Code) >= 0; }
            set
            {
                string current = owner.Value;

                // The order of the codes found in the file is kept, new codes are appended.

                owner.Value = value ? (current.IndexOf(Code) >= 0 ? current : current + Code) : current.Replace(Code.ToString(), "");
            }
        }

        public void Refresh()
        {
            Notify(nameof(IsChecked));
        }
    }

    /// <summary>
    /// One parameter of the configuration file as shown, and edited, in the Configuration page.
    /// </summary>
    public sealed class SettingRow : Observable
    {
        private string value;
        private string originalValue;
        private string context = "";
        private bool isRelevant = true;
        private bool matchesSearch = true;

        public SettingDefinition Definition { get; }

        /// <summary>The server card the parameter belongs to, or null.</summary>
        public ServerCard Server { get; set; }

        /// <summary>The value the parameter had in the text the page has been built from.</summary>
        public string BaseValue { get; set; }

        private bool isPresentInFile = true;

        /// <summary>False when the parameter is not written in the file at all.</summary>
        public bool IsPresentInFile
        {
            get { return isPresentInFile; }
            set { if (isPresentInFile != value) { isPresentInFile = value; Notify(nameof(Hint)); } }
        }

        public event Action<SettingRow> Changed;

        public SettingRow(SettingDefinition definition, string value, string originalValue)
        {
            Definition = definition;

            this.value = value; this.originalValue = originalValue; BaseValue = value;

            if (definition.Kind == SettingKind.HitLogFilter)
            {
                HitLogFlags = ConfigSchema.HitLogCodes.Select(code => new HitLogFlag(this, code.Key, code.Value)).ToList();
            }
        }

        public string Key => Definition.Key;
        public string Label => Definition.Label;
        public string Description => Definition.Description;
        public string Unit => Definition.Unit ?? "";
        public string[] Choices => Definition.Choices;
        public List<HitLogFlag> HitLogFlags { get; }

        public bool IsYesNo => Definition.Kind == SettingKind.YesNo;
        public bool IsChoice => Definition.Kind == SettingKind.Choice;
        public bool IsHitLog => Definition.Kind == SettingKind.HitLogFilter;
        public bool IsText => !IsYesNo && !IsChoice && !IsHitLog;
        public bool IsDomainNameMask => Definition.Kind == SettingKind.DomainNameMask;
        public bool IsWide => Definition.Kind == SettingKind.DomainNameMask || Definition.Kind == SettingKind.QueryTypeMask || Definition.Kind == SettingKind.HitLogFilter || Definition.Key == "HitLogFileName";
        public bool UsesMonoFont => IsText && Definition.Kind != SettingKind.Text;

        public string Value
        {
            get { return value; }
            set
            {
                value = value ?? "";

                if (this.value == value) return;

                this.value = value;

                NotifyAll();

                if (HitLogFlags != null) foreach (HitLogFlag flag in HitLogFlags) flag.Refresh();

                Changed?.Invoke(this);
            }
        }

        public bool BoolValue
        {
            get { return ConfigSchema.ParseYesNo(value); }
            set { if (value != BoolValue) Value = value ? "Yes" : "No"; }
        }

        public string ChoiceValue
        {
            get { return Choices?.FirstOrDefault(c => string.Equals(c, value.Trim(), StringComparison.OrdinalIgnoreCase)); }
            set { if (value != null) Value = value; }
        }

        public string OriginalValue
        {
            get { return originalValue; }
            set { originalValue = value; NotifyAll(); }
        }

        public bool IsModified => value.Trim() != originalValue;

        public string ModifiedToolTip => "Changed and not saved yet. The file says: " + (originalValue.Length == 0 ? "(empty)" : originalValue);

        public string Error => ConfigSchema.Validate(Definition, value);

        public bool HasError => Error != null;

        public string Hint
        {
            get
            {
                if (!isPresentInFile && !IsModified) return "Not written in the file: this is what Acrylic assumes.";

                return ConfigSchema.Humanize(value, Definition.Unit);
            }
        }

        public string Placeholder
        {
            get
            {
                if (Definition.BuiltIn.Length > 0 && Definition.Kind != SettingKind.YesNo && Definition.Kind != SettingKind.Choice) return "empty = " + Definition.BuiltIn;

                return "";
            }
        }

        private bool EqualsDefault
        {
            get
            {
                if (IsYesNo) return ConfigSchema.ParseYesNo(value) == ConfigSchema.ParseYesNo(Definition.Default);

                return string.Equals(value.Trim(), Definition.Default, IsChoice ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
        }

        public bool CanReset => Definition.Category != ConfigSchema.CategoryOther && !EqualsDefault;

        public string ResetToolTip => "Reset to the default value: " + (Definition.Default.Length == 0 ? "(empty)" : Definition.Default);

        public void Reset()
        {
            Value = Definition.Default;
        }

        public override string ToString() { return Label; }

        /// <summary>Where the parameter lives, shown above its name in the search results.</summary>
        public string Context
        {
            get { return context; }
            set { if (context != value) { context = value; Notify(); } }
        }

        public string Location => Server != null ? Definition.Category + "  \u203A  " + Server.Title : Definition.Category;

        /// <summary>False for the parameters that make no sense with the protocol chosen for their server.</summary>
        public bool IsRelevant
        {
            get { return isRelevant; }
            set { if (isRelevant != value) { isRelevant = value; Notify(); Notify(nameof(IsVisible)); } }
        }

        public bool MatchesSearch
        {
            get { return matchesSearch; }
            set { if (matchesSearch != value) { matchesSearch = value; Notify(); Notify(nameof(IsVisible)); } }
        }

        public bool IsVisible => isRelevant && matchesSearch;

        public bool Matches(string[] words)
        {
            foreach (string word in words)
            {
                if (Key.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0 && Label.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0 && Description.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0 && Location.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0 && value.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            return true;
        }
    }

    public sealed class ServerPreset
    {
        public string Name, Address, Port, Protocol, DoHHost, DoHPath;

        public static readonly ServerPreset[] All =
        {
            new ServerPreset { Name = "Google Public DNS (8.8.8.8)", Address = "8.8.8.8", Port = "53", Protocol = "UDP" },
            new ServerPreset { Name = "Google Public DNS, secondary (8.8.4.4)", Address = "8.8.4.4", Port = "53", Protocol = "UDP" },
            new ServerPreset { Name = "Cloudflare (1.1.1.1)", Address = "1.1.1.1", Port = "53", Protocol = "UDP" },
            new ServerPreset { Name = "Quad9 (9.9.9.9)", Address = "9.9.9.9", Port = "53", Protocol = "UDP" },
            null,
            new ServerPreset { Name = "Google Public DNS over HTTPS", Address = "8.8.8.8", Port = "443", Protocol = "DOH", DoHHost = "dns.google", DoHPath = "dns-query" },
            new ServerPreset { Name = "Cloudflare over HTTPS", Address = "1.1.1.1", Port = "443", Protocol = "DOH", DoHHost = "cloudflare-dns.com", DoHPath = "dns-query" },
            new ServerPreset { Name = "Quad9 over HTTPS", Address = "9.9.9.9", Port = "443", Protocol = "DOH", DoHHost = "dns.quad9.net", DoHPath = "dns-query" }
        };
    }

    /// <summary>
    /// The parameters of one of the ten DNS servers, shown as a collapsible card.
    /// </summary>
    public sealed class ServerCard : Observable
    {
        private bool isExpanded;
        private bool matchesSearch = true;

        public int Index { get; }
        public string Title { get; }
        public List<SettingRow> Rows { get; } = new List<SettingRow>();

        public ServerCard(int index)
        {
            Index = index;
            Title = ConfigSchema.ServerOrdinals[index] + " server";
        }

        public SettingRow Row(string key)
        {
            return Rows.First(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public SettingRow Address => Row(ConfigSchema.ServerKey(Index, "Address"));
        public SettingRow Port => Row(ConfigSchema.ServerKey(Index, "Port"));
        public SettingRow Protocol => Row(ConfigSchema.ServerKey(Index, "Protocol"));

        public void Attach()
        {
            foreach (SettingRow row in Rows)
            {
                row.Server = this;
                row.Changed += changed => Refresh();
            }

            Refresh();
        }

        public bool IsExpanded
        {
            get { return isExpanded; }
            set { if (isExpanded != value) { isExpanded = value; Notify(); } }
        }

        public bool MatchesSearch
        {
            get { return matchesSearch; }
            set { if (matchesSearch != value) { matchesSearch = value; Notify(); } }
        }

        private string ProtocolName => (Protocol.ChoiceValue ?? "UDP").ToUpperInvariant();

        /// <summary>
        /// The service uses a server only when its address, its port and its protocol are all specified.
        /// </summary>
        public bool IsInUse => Address.Value.Trim().Length > 0 && Port.Value.Trim().Length > 0 && Protocol.Value.Trim().Length > 0;

        public bool HasErrors => Rows.Any(r => r.IsRelevant && r.HasError);

        private string Incomplete
        {
            get
            {
                if (Address.Value.Trim().Length == 0) return null;

                if (Port.Value.Trim().Length == 0) return "Ignored: no port";
                if (Protocol.Value.Trim().Length == 0) return "Ignored: no protocol";

                if (ProtocolName == "DOH" && Row(ConfigSchema.ServerKey(Index, "DoHProtocolHost")).Value.Trim().Length == 0) return "DoH host name missing";
                if (ProtocolName == "SOCKS5" && Row(ConfigSchema.ServerKey(Index, "Socks5ProtocolProxyAddress")).Value.Trim().Length == 0) return "SOCKS 5 proxy missing";

                return null;
            }
        }

        public string Summary
        {
            get
            {
                string address = Address.Value.Trim();

                if (address.Length == 0) return "";

                string summary = address + "  \u00B7  port " + Port.Value.Trim() + "  \u00B7  " + (ProtocolName == "DOH" ? "DNS-over-HTTPS" : ProtocolName);

                if (ProtocolName == "DOH")
                {
                    string host = Row(ConfigSchema.ServerKey(Index, "DoHProtocolHost")).Value.Trim();

                    if (host.Length > 0) summary += "  \u00B7  " + host;
                }

                return summary;
            }
        }

        public string ChipText => HasErrors ? "Needs attention" : Incomplete ?? (IsInUse ? "In use" : "Not used");

        /// <summary>The colour the state of the server is shown in (see the ToneChip style).</summary>
        public string Tone => (HasErrors || Incomplete != null) ? Tones.Amber : IsInUse ? Tones.Green : "";

        public void Refresh()
        {
            string protocol = ProtocolName;

            foreach (SettingRow row in Rows)
            {
                row.IsRelevant = row.Definition.RelevantProtocol == null || row.Definition.RelevantProtocol == protocol;
            }

            Notify(nameof(Summary)); Notify(nameof(ChipText)); Notify(nameof(Tone));
        }

        public void Apply(ServerPreset preset)
        {
            Address.Value = preset.Address;
            Port.Value = preset.Port;
            Protocol.Value = preset.Protocol;

            if (preset.Protocol == "DOH")
            {
                Row(ConfigSchema.ServerKey(Index, "DoHProtocolHost")).Value = preset.DoHHost;
                Row(ConfigSchema.ServerKey(Index, "DoHProtocolPath")).Value = preset.DoHPath;
            }

            IsExpanded = true;
        }

        public void Clear()
        {
            Address.Value = "";
        }

        public override string ToString() { return Title; }
    }

    public sealed class AllowedAddressRow : Observable
    {
        private string value;

        public event Action Changed;

        public AllowedAddressRow(string value)
        {
            this.value = value;
        }

        public string Value
        {
            get { return value; }
            set { if (this.value != value) { this.value = value ?? ""; Notify(); Changed?.Invoke(); } }
        }
    }

    public sealed class CategoryItem : Observable
    {
        private int modifiedCount;

        public string Name { get; }
        public string Glyph { get; }
        public string Description { get; }

        public CategoryItem(string name, string glyph, string description)
        {
            Name = name; Glyph = glyph; Description = description;
        }

        public int ModifiedCount
        {
            get { return modifiedCount; }
            set { if (modifiedCount != value) { modifiedCount = value; Notify(); Notify(nameof(HasModified)); } }
        }

        public bool HasModified => modifiedCount > 0;

        public override string ToString() { return Name; }
    }
}
