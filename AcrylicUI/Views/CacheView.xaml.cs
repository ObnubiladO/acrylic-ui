using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AcrylicUI.Core;

namespace AcrylicUI.Views
{
    /// <summary>
    /// A domain name found in the address cache, with all the responses cached for it.
    /// </summary>
    public sealed class CacheDomainRow
    {
        public string DomainName { get; set; }
        public string Types { get; set; }
        public string Answer { get; set; }
        public string Age { get; set; }
        public string TimeText { get; set; }
        public string State { get; set; }
        public string StateToolTip { get; set; }
        public string Details { get; set; }
        public int Count { get; set; }

        /// <summary>The colour the state is shown in (see the ToneChip style).</summary>
        public string Tone { get; set; }

        public override string ToString() { return DomainName; }

        private string readableName;

        /// <summary>The name as people write it, when the cached one is in its xn-- form.</summary>
        public string ReadableName => readableName ?? (readableName = DomainNames.ToUnicode(DomainName));

        public string NameToolTip => ReadableName == DomainName ? DomainName : DomainName + "\r\nis how DNS writes " + ReadableName;
    }

    /// <summary>
    /// The page that shows the address cache and clears single domain names from it.
    /// </summary>
    public partial class CacheView : UserControl
    {
        private static readonly Regex DomainNamePattern = new Regex(@"^[a-z0-9_]([a-z0-9_\-]*[a-z0-9_])?(\.[a-z0-9_]([a-z0-9_\-]*[a-z0-9_])?)*$", RegexOptions.IgnoreCase);

        private IShell shell;

        private List<CacheDomainRow> allRows = new List<CacheDomainRow>();

        private IPEndPoint localEndPoint;

        private bool canClear = true;
        private bool loading;

        private int scavengingTime, silentUpdateTime, negativeTime, failureTime;

        public CacheView()
        {
            InitializeComponent();

            foreach (string type in new[] { "A", "AAAA", "CNAME", "HTTPS", "MX", "NS", "PTR", "SOA", "SRV", "TXT" }) LookupType.Items.Add(type);

            LookupType.SelectedIndex = 0;

            TableLayout.FillColumn(DomainList, 0, 150);
        }

        public void Initialize(IShell shell)
        {
            this.shell = shell;

            shell.EngineStateChanged += (sender, e) => Reload();

            Reload();
        }

        public void FocusSearch()
        {
            SearchBox.Focus(); SearchBox.SelectAll();
        }

        // ------------------------------------------------------------------------------------------------------------
        // Reading the cache file
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Reads the configuration and the cache file again and refreshes everything shown.
        /// </summary>
        public void Reload()
        {
            if (shell == null) return;

            IniDocument configuration;

            try { configuration = AcrylicOperations.LoadConfiguration(); } catch (Exception) { configuration = IniDocument.Parse(""); }

            scavengingTime = AcrylicOperations.ReadMinutes(configuration, "AddressCacheScavengingTime", 5760);
            silentUpdateTime = AcrylicOperations.ReadMinutes(configuration, "AddressCacheSilentUpdateTime", 1440);
            negativeTime = AcrylicOperations.ReadMinutes(configuration, "AddressCacheNegativeTime", 60);
            failureTime = AcrylicOperations.ReadMinutes(configuration, "AddressCacheFailureTime", 0);

            bool disabled = ConfigSchema.ParseYesNo(configuration.Get(ConfigSchema.GlobalSection, "AddressCacheDisabled"));
            bool inMemoryOnly = ConfigSchema.ParseYesNo(configuration.Get(ConfigSchema.GlobalSection, "AddressCacheInMemoryOnly"));

            localEndPoint = AcrylicOperations.GetLocalEndPoint(configuration);

            bool running = (shell.EngineState == EngineState.Running);

            AddressCacheFile cache;

            string notice = null;

            try
            {
                cache = AddressCacheFile.Load(AcrylicPaths.CacheFile);
            }
            catch (Exception e)
            {
                cache = new AddressCacheFile();

                notice = "AcrylicCache.dat could not be read: " + e.Message;
            }

            canClear = true;

            if (disabled)
            {
                notice = "The address cache is disabled in the configuration (\"Disable the address cache\" is set to Yes), so Acrylic caches nothing and there is nothing to clear.";
                canClear = false;
            }
            else if (inMemoryOnly)
            {
                notice = "The address cache is kept in memory only (\"Keep the address cache in memory only\" is set to Yes). It is never written to disk, so it cannot be listed here and a single domain name cannot be cleared from it: restarting Acrylic empties it altogether.";
                canClear = false;
            }
            else if (!cache.IsWellFormed)
            {
                notice = "AcrylicCache.dat ends with data that is not an address cache record. The readable part is listed, but to stay safe nothing can be cleared from it. \"Purge everything\" replaces the file.";
                canClear = false;
            }

            NoticeText.Text = notice ?? "";
            NoticePanel.Visibility = notice != null ? Visibility.Visible : Visibility.Collapsed;

            BuildRows(cache);

            // Texts that depend on what Acrylic is doing

            if (!cache.Exists)
            {
                SnapshotText.Text = "There is no cache file yet. Acrylic keeps its address cache in memory and writes it to AcrylicCache.dat when it stops." + (running ? " \"Update the list\" restarts Acrylic for a moment to do that." : "");
            }
            else
            {
                SnapshotText.Text = "The address cache as Acrylic saved it on " + cache.LastWriteTime.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture) + "."
                    + (running ? " Acrylic keeps its cache in memory and saves it only when it stops, so what it resolved since then is not listed. \"Update the list\" restarts Acrylic for a moment to bring the list up to date." : " Acrylic is not running, so this is the whole cache.");
            }

            RefreshButton.ToolTip = running ? "Restarts Acrylic for a moment, which makes it save its address cache, and reads the cache again" : "Reads the cache file again";

            int responses = cache.Entries.Count;
            int unreadable = cache.Entries.Count(entry => entry.DomainName.Length == 0);

            SummaryText.Text = !cache.Exists ? "" : allRows.Count + (allRows.Count == 1 ? " domain name" : " domain names") + "   \u00B7   " + responses + (responses == 1 ? " cached response" : " cached responses") + "   \u00B7   " + FormatSize(cache.FileSize) + (unreadable > 0 ? "   \u00B7   " + unreadable + " not readable" : "");

            ClearNote.Text = !canClear ? "" : running ? "Acrylic keeps its cache in memory, so it is restarted for a moment (about a second) to do this." : "Acrylic is not running: the cache file is changed directly.";

            LookupIntro.Text = localEndPoint == null
                ? "Acrylic is not bound to any local address, so there is nothing to send a DNS request to."
                : "Sends a DNS request to Acrylic at " + localEndPoint + ", like any program on this computer would. Repeat it to see the cache at work.";

            LookupButton.IsEnabled = (localEndPoint != null);
            PurgeButton.IsEnabled = cache.Exists || running;

            UpdateClearControls();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " bytes";

            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";

            return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        private void BuildRows(AddressCacheFile cache)
        {
            DateTime now = DateTime.Now;

            var rows = new List<CacheDomainRow>();

            foreach (var group in cache.Entries.Where(entry => entry.DomainName.Length > 0).GroupBy(entry => entry.DomainName.ToLowerInvariant()).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<CacheEntry> entries = group.OrderByDescending(entry => entry.Time).ToList();

                // The response shown for the domain name is the one people usually care about: its address.

                CacheEntry shown = entries.FirstOrDefault(entry => entry.QueryType == DnsPacket.TypeA && entry.Kind == CacheResponseKind.Positive) ?? entries.FirstOrDefault(entry => entry.Kind == CacheResponseKind.Positive) ?? entries[0];

                var row = new CacheDomainRow
                {
                    DomainName = group.Key,
                    Count = entries.Count,
                    Types = string.Join(", ", entries.Select(entry => entry.QueryType).Distinct().OrderBy(type => type).Select(DnsPacket.TypeName)),
                    Answer = shown.Answer,
                    Age = FormatAge(now - shown.Time),
                    TimeText = "Cached on " + shown.Time.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture),
                    Details = string.Join("\r\n", entries.Take(12).Select(entry => DnsPacket.TypeName(entry.QueryType).PadRight(6) + entry.Answer + "   (" + FormatAge(now - entry.Time) + ")")) + (entries.Count > 12 ? "\r\n..." : "")
                };

                DescribeState(shown, now, row);

                rows.Add(row);
            }

            allRows = rows;

            ApplyFilter();
        }

        private void DescribeState(CacheEntry entry, DateTime now, CacheDomainRow row)
        {
            double age = Math.Max(0, (now - entry.Time).TotalMinutes);

            if (entry.Kind == CacheResponseKind.Positive)
            {
                if (age < silentUpdateTime) { row.State = "Fresh"; row.Tone = "Green"; row.StateToolTip = "Answered from the cache, with no network activity."; }
                else if (age < scavengingTime) { row.State = "Update due"; row.Tone = "Accent"; row.StateToolTip = "Still answered from the cache; on the next request Acrylic also refreshes it silently from your DNS servers."; }
                else { row.State = "Expired"; row.StateToolTip = "Older than the lifetime of a positive response: Acrylic resolves it again on the next request."; }
            }
            else if (entry.Kind == CacheResponseKind.Negative)
            {
                if (age < negativeTime) { row.State = "Negative"; row.Tone = "Amber"; row.StateToolTip = "Acrylic remembers that the answer was negative, for example that the name does not exist."; }
                else { row.State = "Expired"; row.StateToolTip = "Older than the lifetime of a negative response: Acrylic resolves it again on the next request."; }
            }
            else
            {
                if (age < failureTime) { row.State = "Failure"; row.Tone = "Red"; row.StateToolTip = "Acrylic remembers that your DNS servers failed to answer this request."; }
                else { row.State = "Expired"; row.StateToolTip = "Older than the lifetime of a failure response: Acrylic resolves it again on the next request."; }
            }
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalMinutes < 1) return "just now";
            if (age.TotalMinutes < 60) return (int)age.TotalMinutes + " min ago";
            if (age.TotalHours < 24) return (int)age.TotalHours + " h ago";

            int days = (int)age.TotalDays;

            return days + (days == 1 ? " day ago" : " days ago");
        }

        private void ApplyFilter()
        {
            string[] words = SearchBox.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            List<CacheDomainRow> shown = words.Length == 0 ? allRows : allRows.Where(row => words.All(word => row.DomainName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 || row.ReadableName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 || row.Answer.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            loading = true;

            DomainList.ItemsSource = shown;

            loading = false;

            if (shown.Count == 0)
            {
                EmptyText.Text = allRows.Count == 0 ? "No cached domain name to show." : "No cached domain name matches \"" + SearchBox.Text.Trim() + "\".\r\nIt may still be in the memory of Acrylic: use \"Clear a domain name\" on the right, or update the list.";
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyText.Visibility = Visibility.Collapsed;
            }

            UpdateClearControls();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (shell != null) ApplyFilter();
        }

        private void DomainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading) UpdateClearControls();
        }

        private void UpdateClearControls()
        {
            int selected = DomainList.SelectedItems.Count;

            ClearSelectedButton.Visibility = (selected > 0 && canClear) ? Visibility.Visible : Visibility.Hidden;
            ClearSelectedButton.Content = selected == 1 ? "Clear the selected domain name" : "Clear the " + selected + " selected domain names";

            ClearBox.IsEnabled = SubdomainsBox.IsEnabled = canClear;
            ClearButton.IsEnabled = canClear && ClearBox.Text.Trim().Length > 0;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Clearing
        // ------------------------------------------------------------------------------------------------------------

        private void ClearBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (shell != null) UpdateClearControls();
        }

        private async void ClearBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !ClearButton.IsEnabled) return;

            e.Handled = true;

            await ClearTypedAsync();
        }

        private async void Clear_Click(object sender, RoutedEventArgs e)
        {
            await ClearTypedAsync();
        }

        private async Task ClearTypedAsync()
        {
            string domainName = AddressCacheFile.NormalizeDomainName(ClearBox.Text);

            if (!DomainNamePattern.IsMatch(domainName))
            {
                ShowClearResult("Type a domain name like example.com.", "Amber");

                return;
            }

            if (await ClearAsync(new List<string> { domainName }, SubdomainsBox.IsChecked == true)) ClearBox.Text = "";
        }

        private async void ClearRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CacheDomainRow row) await ClearAsync(new List<string> { row.DomainName }, false);
        }

        private async void ClearSelected_Click(object sender, RoutedEventArgs e)
        {
            await ClearSelectedAsync();
        }

        private async void DomainList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None) return;

            e.Handled = true;

            await ClearSelectedAsync();
        }

        private async Task ClearSelectedAsync()
        {
            List<string> domainNames = DomainList.SelectedItems.Cast<CacheDomainRow>().Select(row => row.DomainName).ToList();

            if (domainNames.Count > 0) await ClearAsync(domainNames, false);
        }

        /// <summary>
        /// Clears the domain names from the cache. Returns true if something has been removed.
        /// </summary>
        private async Task<bool> ClearAsync(List<string> domainNames, bool includeSubdomains)
        {
            if (!canClear) return false;

            List<CacheEntry> removed = null;

            bool succeeded = await shell.RunAsync("An error occurred while clearing the address cache", progress => removed = shell.Operations.ClearCachedDomains(domainNames, includeSubdomains, progress));

            Reload();

            if (!succeeded) { ClearResultPanel.Visibility = Visibility.Collapsed; return false; }

            string what = domainNames.Count == 1 ? domainNames[0] + (includeSubdomains ? " and its subdomains" : "") : domainNames.Count + " domain names";

            string message;

            if (removed.Count == 0)
            {
                message = domainNames.Count == 1 ? "Nothing was cached for " + what + "." : "Nothing was cached for these " + what + ".";

                ShowClearResult(message, "Amber");
            }
            else
            {
                string types = string.Join(", ", removed.Select(entry => DnsPacket.TypeName(entry.QueryType)).Distinct());

                message = "Cleared " + what + ": " + removed.Count + (removed.Count == 1 ? " cached response" : " cached responses") + " removed (" + types + ").";

                ShowClearResult(message, "Green");
            }

            shell.SetStatus(message);

            return removed.Count > 0;
        }

        private void ShowClearResult(string text, string tone)
        {
            ClearResultText.Text = text;
            Tones.Apply(ClearResultPanel, ClearResultText, tone);
            ClearResultPanel.Visibility = Visibility.Visible;
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (shell.EngineState == EngineState.Running)
            {
                if (await shell.RunAsync("An error occurred while restarting Acrylic", progress => shell.Operations.FlushCache(progress))) shell.SetStatus("Acrylic has been restarted and its address cache has been read again.");
            }
            else
            {
                shell.SetStatus("The address cache has been read again.");
            }

            Reload();
        }

        private async void Purge_Click(object sender, RoutedEventArgs e)
        {
            if (shell.Window is MainWindow window && await window.PurgeCacheAsync()) ClearResultPanel.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Trying a lookup
        // ------------------------------------------------------------------------------------------------------------

        private async void LookupBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !LookupButton.IsEnabled) return;

            e.Handled = true;

            await LookupAsync();
        }

        private async void Lookup_Click(object sender, RoutedEventArgs e)
        {
            await LookupAsync();
        }

        private async Task LookupAsync()
        {
            string domainName = AddressCacheFile.NormalizeDomainName(LookupBox.Text);

            IPEndPoint endPoint = localEndPoint;

            if (endPoint == null) return;

            LookupResultPanel.Visibility = Visibility.Visible;
            LookupResultText.Visibility = Visibility.Collapsed;

            if (!DomainNamePattern.IsMatch(domainName))
            {
                ShowLookupResult("Type a domain name like www.example.com.", null, "Amber");

                return;
            }

            ushort type = DnsPacket.ParseTypeName((string)LookupType.SelectedItem);

            LookupButton.IsEnabled = false;

            ShowLookupResult("Asking Acrylic...", null, null);

            DnsLookupResult result = await Task.Run(() => DnsClient.Query(endPoint, domainName, type, 6000));

            LookupButton.IsEnabled = true;

            string subject = domainName + " (" + DnsPacket.TypeName(type) + ")";

            if (result.Error != null)
            {
                ShowLookupResult(subject + ": " + result.Error, null, "Red");
            }
            else if (result.Response.ResponseCode != 0)
            {
                ShowLookupResult(subject + ": " + DnsPacket.ResponseCodeName(result.Response.ResponseCode) + ", in " + result.ElapsedMilliseconds + " ms.", null, "Amber");
            }
            else if (result.Response.Answers.Count == 0)
            {
                ShowLookupResult(subject + ": no data, in " + result.ElapsedMilliseconds + " ms.", null, "Amber");
            }
            else
            {
                ShowLookupResult(subject + " answered in " + result.ElapsedMilliseconds + " ms.", string.Join("\r\n", result.Response.Answers.Take(10).Select(answer => DnsPacket.TypeName(answer.Type).PadRight(6) + answer.Text)), "Green");
            }
        }

        private void ShowLookupResult(string title, string details, string tone)
        {
            LookupResultTitle.Text = title;
            Tones.Apply(LookupResultPanel, LookupResultTitle, tone);

            LookupResultText.Text = details ?? "";
            LookupResultText.Visibility = details != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
