using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AcrylicUI.Core;

namespace AcrylicUI.Views
{
    /// <summary>
    /// The page that edits AcrylicConfiguration.ini, either parameter by parameter or as plain text.
    /// </summary>
    public partial class ConfigurationView : UserControl, IEditorPage
    {
        private IShell shell;

        private TextFile file = new TextFile();

        private string savedText = "";    // What is in the file on disk.
        private string workingText = "";  // What the parameter by parameter editor has been built from.

        private List<SettingRow> rows = new List<SettingRow>();
        private List<ServerCard> servers = new List<ServerCard>();
        private List<string> baseAllowedAddresses = new List<string>();

        private readonly ObservableCollection<AllowedAddressRow> allowedAddresses = new ObservableCollection<AllowedAddressRow>();
        private readonly ObservableCollection<CategoryItem> categories = new ObservableCollection<CategoryItem>();

        private bool building;
        private bool textMode;
        private bool dirty;

        private string categoryBeforeSearch;

        public ConfigurationView()
        {
            InitializeComponent();

            CategoryList.ItemsSource = categories;
            AllowedList.ItemsSource = allowedAddresses;

            allowedAddresses.CollectionChanged += (sender, e) => OnEdited();
        }

        public void Initialize(IShell shell)
        {
            this.shell = shell;

            LoadFromDisk();
        }

        // ------------------------------------------------------------------------------------------------------------
        // IEditorPage
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>The plain text editor of the page.</summary>
        public TextBox Editor => TextEditor;

        public string FileName => "AcrylicConfiguration.ini";

        public bool IsDirty => dirty;

        public event EventHandler DirtyChanged;

        public void FocusSearch()
        {
            if (textMode) TextEditor.Focus(); else { SearchBox.Focus(); SearchBox.SelectAll(); }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------------------------------------------------

        private void LoadFromDisk()
        {
            NoticePanel.Visibility = Visibility.Collapsed;

            try
            {
                if (File.Exists(AcrylicPaths.ConfigurationFile))
                {
                    file = TextFile.Load(AcrylicPaths.ConfigurationFile);
                }
                else
                {
                    file = new TextFile();

                    ShowNotice("AcrylicConfiguration.ini was not found in \"" + AcrylicPaths.Directory + "\". It will be created when you save.");
                }
            }
            catch (Exception e)
            {
                file = new TextFile();

                ShowNotice("AcrylicConfiguration.ini could not be read: " + e.Message);
            }

            savedText = workingText = file.Text;

            building = true;

            TextEditor.Text = savedText;

            building = false;

            BuildStructured(null);

            UpdateDirty();
        }

        private void ShowNotice(string text)
        {
            NoticeText.Text = text;
            NoticePanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Builds the parameter by parameter editor from the working text.
        /// </summary>
        private void BuildStructured(string categoryToSelect)
        {
            building = true;

            IniDocument savedDocument = IniDocument.Parse(savedText);
            IniDocument workingDocument = IniDocument.Parse(workingText);

            var expanded = new HashSet<int>(servers.Where(s => s.IsExpanded).Select(s => s.Index));

            bool firstBuild = (servers.Count == 0);

            rows = new List<SettingRow>();
            servers = new List<ServerCard>();

            for (int i = 0; i < ConfigSchema.ServerOrdinals.Length; i++) servers.Add(new ServerCard(i));

            foreach (SettingDefinition definition in ConfigSchema.Definitions)
            {
                SettingRow row = CreateRow(definition, savedDocument, workingDocument);

                rows.Add(row);

                if (definition.ServerIndex >= 0) servers[definition.ServerIndex].Rows.Add(row);
            }

            // Whatever else is written in the section is shown as it is, so that nothing in the file is out of reach.

            foreach (var entry in workingDocument.GetEntries(ConfigSchema.GlobalSection))
            {
                if (ConfigSchema.Find(entry.Key) != null || rows.Any(r => string.Equals(r.Key, entry.Key, StringComparison.OrdinalIgnoreCase))) continue;

                rows.Add(CreateRow(new SettingDefinition { Key = entry.Key, Label = entry.Key, Category = ConfigSchema.CategoryOther, Kind = SettingKind.Text, Description = "This parameter is not known to this version of the Acrylic UI. It is kept as it is." }, savedDocument, workingDocument));
            }

            foreach (ServerCard server in servers)
            {
                server.Attach();

                server.IsExpanded = firstBuild ? (server.Index == 0 && server.IsInUse) : expanded.Contains(server.Index);
            }

            foreach (SettingRow row in rows) row.Changed += changed => OnEdited();

            baseAllowedAddresses = workingDocument.GetEntries(ConfigSchema.AllowedAddressesSection).Select(entry => entry.Value).ToList();

            allowedAddresses.Clear();

            foreach (string address in baseAllowedAddresses) AddAllowedRow(address);

            // Categories

            string selected = categoryToSelect ?? (CategoryList.SelectedItem as CategoryItem)?.Name ?? categoryBeforeSearch ?? ConfigSchema.CategoryServers;

            SearchBox.Text = "";

            categories.Clear();

            categories.Add(new CategoryItem(ConfigSchema.CategoryServers, "\uE968", "Requests that cannot be answered from the hosts file or from the address cache are forwarded to all of the DNS servers configured here at the same time, and the first valid response wins. Up to ten DNS servers can be used."));
            categories.Add(new CategoryItem(ConfigSchema.CategoryCache, "\uE8F1", "Acrylic remembers the responses of your DNS servers. A recent item is answered from the cache without any network activity. An item older than the silent update time is still answered from the cache immediately, while Acrylic refreshes it in the background. An item past its lifetime is resolved again. To minimize disk activity the cache is saved to disk only when Acrylic stops."));
            categories.Add(new CategoryItem(ConfigSchema.CategoryRequests, "\uE8AB", "How Acrylic answers some kinds of requests by itself."));
            categories.Add(new CategoryItem(ConfigSchema.CategoryNetwork, "\uE839", "The addresses and the ports Acrylic listens to for DNS requests."));
            categories.Add(new CategoryItem(ConfigSchema.CategoryTimeouts, "\uE916", "How long Acrylic waits for your DNS servers."));
            categories.Add(new CategoryItem(ConfigSchema.CategoryHitLog, "\uE9D9", "The hit log is a text file into which every DNS request and DNS response received by Acrylic can be logged. Each line holds these TAB separated fields: the timestamp, the IP address of the client, the status code, the index of the DNS server, the time it took to respond and the dissected DNS request or response."));
            categories.Add(new CategoryItem(ConfigSchema.CategoryAllowed, "\uE72E", "For security reasons Acrylic refuses to handle the requests coming from other computers. List here the IP addresses (wildcards are allowed) Acrylic may accept requests from. You must also create a firewall rule allowing the incoming traffic directed to AcrylicService.exe and AcrylicConsole.exe."));

            if (rows.Any(r => r.Definition.Category == ConfigSchema.CategoryOther)) categories.Add(new CategoryItem(ConfigSchema.CategoryOther, "\uE712", "Parameters found in the [GlobalSection] of the file that this version of the Acrylic UI does not know."));

            CategoryList.SelectedItem = categories.FirstOrDefault(c => c.Name == selected) ?? categories[0];

            building = false;

            ApplySearch();
            UpdateModifiedCounts();
        }

        private static SettingRow CreateRow(SettingDefinition definition, IniDocument savedDocument, IniDocument workingDocument)
        {
            string working = workingDocument.Get(ConfigSchema.GlobalSection, definition.Key);
            string saved = savedDocument.Get(ConfigSchema.GlobalSection, definition.Key);

            // A parameter missing from the file is shown with the value the Acrylic service assumes for it.

            return new SettingRow(definition, working ?? definition.BuiltIn, saved ?? definition.BuiltIn) { IsPresentInFile = (working != null) };
        }

        private void AddAllowedRow(string value)
        {
            var row = new AllowedAddressRow(value);

            row.Changed += OnEdited;

            allowedAddresses.Add(row);
        }

        // ------------------------------------------------------------------------------------------------------------
        // What is shown
        // ------------------------------------------------------------------------------------------------------------

        private string[] SearchWords => SearchBox.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (building) return;

            if (CategoryList.SelectedItem == null) return;

            if (SearchBox.Text.Length > 0)
            {
                // Picking a category ends the search.

                building = true; SearchBox.Text = ""; building = false;
            }

            ApplySearch();

            PaneScroller.ScrollToTop();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (building) return;

            building = true;

            if (SearchWords.Length > 0)
            {
                // While the search results are shown no category is the current one.

                if (CategoryList.SelectedItem != null) { categoryBeforeSearch = ((CategoryItem)CategoryList.SelectedItem).Name; CategoryList.SelectedItem = null; }
            }
            else if (CategoryList.SelectedItem == null)
            {
                CategoryList.SelectedItem = categories.FirstOrDefault(c => c.Name == categoryBeforeSearch) ?? categories[0];
            }

            building = false;

            ApplySearch();

            PaneScroller.ScrollToTop();
        }

        private void ApplySearch()
        {
            string[] words = SearchWords;

            var category = CategoryList.SelectedItem as CategoryItem;

            if (words.Length > 0)
            {
                List<SettingRow> matches = rows.Where(r => r.Matches(words)).ToList();

                foreach (SettingRow row in rows) { row.MatchesSearch = true; row.Context = row.Location; }

                PaneTitle.Text = "Search results";
                PaneDescription.Text = matches.Count == 1 ? "1 parameter matches." : matches.Count + " parameters match.";
                SearchSummary.Text = "";

                ServerList.ItemsSource = null;
                ServerList.Visibility = Visibility.Collapsed;

                RowList.ItemsSource = matches;
                RowsCard.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                AllowedCard.Visibility = Visibility.Collapsed;
                NoResultsText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                return;
            }

            foreach (SettingRow row in rows) { row.MatchesSearch = true; row.Context = ""; }

            NoResultsText.Visibility = Visibility.Collapsed;
            SearchSummary.Text = "";

            if (category == null) return;

            PaneTitle.Text = category.Name;
            PaneDescription.Text = category.Description;

            bool isServers = (category.Name == ConfigSchema.CategoryServers);
            bool isAllowed = (category.Name == ConfigSchema.CategoryAllowed);

            ServerList.ItemsSource = isServers ? servers : null;
            ServerList.Visibility = isServers ? Visibility.Visible : Visibility.Collapsed;

            RowList.ItemsSource = (isServers || isAllowed) ? null : rows.Where(r => r.Definition.Category == category.Name).ToList();
            RowsCard.Visibility = (isServers || isAllowed) ? Visibility.Collapsed : Visibility.Visible;

            AllowedCard.Visibility = isAllowed ? Visibility.Visible : Visibility.Collapsed;

            UpdateAllowedHints();
        }

        private void UpdateAllowedHints()
        {
            AllowedEmptyText.Visibility = allowedAddresses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AllowedWarning.Visibility = allowedAddresses.Any(a => a.Value.Trim() == "*") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateModifiedCounts()
        {
            foreach (CategoryItem category in categories)
            {
                if (category.Name == ConfigSchema.CategoryAllowed) category.ModifiedCount = CurrentAllowedAddresses().SequenceEqual(IniDocument.Parse(savedText).GetEntries(ConfigSchema.AllowedAddressesSection).Select(entry => entry.Value)) ? 0 : 1;
                else category.ModifiedCount = rows.Count(r => r.Definition.Category == category.Name && r.IsModified);
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------------------------------------------------

        private void OnEdited()
        {
            if (building) return;

            UpdateAllowedHints();
            UpdateModifiedCounts();
            UpdateDirty();
        }

        private List<string> CurrentAllowedAddresses()
        {
            return allowedAddresses.Select(a => a.Value.Trim()).Where(a => a.Length > 0).ToList();
        }

        /// <summary>
        /// Returns the text of the file with the pending changes applied to it.
        /// </summary>
        private string BuildText()
        {
            if (textMode) return TextEditor.Text;

            IniDocument document = IniDocument.Parse(workingText);

            foreach (SettingRow row in rows)
            {
                string value = row.Value.Trim();

                // Parameters left alone are not written at all: this matters for the ones missing from the file.

                if (value != row.BaseValue) document.Set(ConfigSchema.GlobalSection, row.Key, value);
            }

            List<string> allowed = CurrentAllowedAddresses();

            if (!allowed.SequenceEqual(baseAllowedAddresses))
            {
                document.ReplaceEntries(ConfigSchema.AllowedAddressesSection, allowed.Select((address, index) => new KeyValuePair<string, string>("IP" + (index + 1), address)).ToList());
            }

            return document.ToText();
        }

        private void UpdateDirty()
        {
            bool isDirty = BuildText() != savedText;

            UnsavedChip.Visibility = isDirty ? Visibility.Visible : Visibility.Collapsed;

            SaveButton.IsEnabled = isDirty;
            DiscardButton.IsEnabled = isDirty;

            if (isDirty != dirty)
            {
                dirty = isDirty;

                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement)?.DataContext as SettingRow)?.Reset();
        }

        private void TestMask_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SettingRow row) new PatternTesterDialog(PatternTesterMode.AffinityMask, row.Value) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var server = (ServerCard)button.DataContext;

            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };

            foreach (ServerPreset preset in ServerPreset.All)
            {
                if (preset == null) { menu.Items.Add(new Separator()); continue; }

                var item = new MenuItem { Header = preset.Name };

                item.Click += (s, args) => server.Apply(preset);

                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void ClearServer_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement)?.DataContext as ServerCard)?.Clear();
        }

        private void AddAllowed_Click(object sender, RoutedEventArgs e)
        {
            AddAllowedRow("");

            // Put the caret in the box just added.

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = AllowedList.ItemContainerGenerator.ContainerFromIndex(allowedAddresses.Count - 1) as FrameworkElement;

                if (container != null) FindChild<TextBox>(container)?.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void RemoveAllowed_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AllowedAddressRow row) allowedAddresses.Remove(row);
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is T match) return match;

                T nested = FindChild<T>(child);

                if (nested != null) return nested;
            }

            return null;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Text mode
        // ------------------------------------------------------------------------------------------------------------

        private void TextMode_Changed(object sender, RoutedEventArgs e)
        {
            bool wanted = (TextModeButton.IsChecked == true);

            if (shell == null || wanted == textMode) return;

            if (wanted)
            {
                string text = BuildText();

                textMode = true;
                building = true;

                TextEditor.Text = text;

                building = false;

                StructuredPanel.Visibility = Visibility.Collapsed;
                TextPanel.Visibility = Visibility.Visible;
                SearchBox.IsEnabled = false;

                TextEditor.Focus();
            }
            else
            {
                workingText = TextEditor.Text;

                textMode = false;

                BuildStructured(null);

                TextPanel.Visibility = Visibility.Collapsed;
                StructuredPanel.Visibility = Visibility.Visible;
                SearchBox.IsEnabled = true;
            }

            UpdateDirty();
        }

        private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (building || !textMode) return;

            UpdateDirty();
        }

        // ------------------------------------------------------------------------------------------------------------
        // Saving
        // ------------------------------------------------------------------------------------------------------------

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        private void Discard_Click(object sender, RoutedEventArgs e)
        {
            if (!dirty) return;

            if (MessageDialog.Show(Window.GetWindow(this), "Discard every change made to AcrylicConfiguration.ini since the last save?", App.Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;

            LoadFromDisk();

            shell.SetStatus("The changes made to AcrylicConfiguration.ini have been discarded.");
        }

        public bool Save()
        {
            if (!dirty) return true;

            if (!textMode)
            {
                List<SettingRow> invalid = rows.Where(r => r.IsRelevant && r.HasError).ToList();

                if (invalid.Count > 0)
                {
                    Reveal(invalid[0]);

                    string list = string.Join("\r\n", invalid.Take(6).Select(r => "  \u2022  " + (r.Server != null ? r.Server.Title + ", " : "") + r.Label + ": " + r.Error));

                    MessageDialog.Show(Window.GetWindow(this), (invalid.Count == 1 ? "One value is not valid" : invalid.Count + " values are not valid") + ", so the file has not been saved:\r\n\r\n" + list + (invalid.Count > 6 ? "\r\n  ..." : ""), App.Title, MessageBoxButton.OK, MessageBoxImage.Warning);

                    return false;
                }
            }

            string text = BuildText();

            try
            {
                shell.SetStatus("Saving changes to file \"" + AcrylicPaths.ConfigurationFile + "\"...");

                file.Text = text;
                file.Save(AcrylicPaths.ConfigurationFile);
            }
            catch (Exception e)
            {
                shell.SetStatus("An error occurred while saving changes to file \"" + AcrylicPaths.ConfigurationFile + "\".");

                MessageDialog.Show(Window.GetWindow(this), "AcrylicConfiguration.ini could not be saved.\r\n\r\n" + e.Message, App.Title, MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }

            savedText = workingText = text;

            NoticePanel.Visibility = Visibility.Collapsed;

            if (!textMode)
            {
                // What is shown is now what is saved.

                building = true;

                foreach (SettingRow row in rows)
                {
                    string value = row.Value.Trim();

                    if (value != row.BaseValue) row.IsPresentInFile = true;

                    row.BaseValue = value;
                    row.OriginalValue = value;
                }

                baseAllowedAddresses = CurrentAllowedAddresses();

                building = false;

                UpdateModifiedCounts();
            }

            UpdateDirty();

            shell.SetStatus("Changes saved to file \"" + AcrylicPaths.ConfigurationFile + "\" successfully.");
            shell.NotifyFileSaved(FileName);

            return true;
        }

        /// <summary>
        /// Brings a parameter into view.
        /// </summary>
        private void Reveal(SettingRow row)
        {
            building = true;

            SearchBox.Text = "";
            CategoryList.SelectedItem = categories.FirstOrDefault(c => c.Name == row.Definition.Category);

            building = false;

            if (row.Server != null) row.Server.IsExpanded = true;

            ApplySearch();
        }
    }
}
