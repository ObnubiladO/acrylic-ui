using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AcrylicUI.Core;

namespace AcrylicUI.Views
{
    /// <summary>
    /// One line of the hosts file as shown in the table of the Hosts page.
    /// </summary>
    public sealed class HostsRow : Observable
    {
        private readonly HostsView owner;

        public HostsLine Line { get; }
        public int Index { get; }

        /// <summary>A problem that does not depend on the text of the line alone, like a missing included file.</summary>
        public string ExtraProblem { get; set; }

        public HostsRow(HostsView owner, HostsLine line, int index)
        {
            this.owner = owner; Line = line; Index = index;
        }

        public int LineNumber => Index + 1;

        public bool IsEntry => Line.Kind == HostsLineKind.Rule || Line.Kind == HostsLineKind.Include;

        public bool IsComment => Line.Kind == HostsLineKind.Comment;

        public bool Enabled
        {
            get { return IsEntry && !Line.Disabled; }
            set { if (IsEntry && value != Enabled) owner.SetEnabled(new[] { this }, value); }
        }

        public double TextOpacity => (IsEntry && Line.Disabled) ? 0.45 : 1.0;

        public string ActionText
        {
            get
            {
                if (Line.Kind == HostsLineKind.Include) return "Include";

                if (Line.Kind != HostsLineKind.Rule) return "";

                return Line.IsForward ? "Forward" : Line.IsBlock ? "Block" : "Answer";
            }
        }

        /// <summary>The colour the action is shown in (see the ToneChip style).</summary>
        public string Tone => Line.Kind == HostsLineKind.Include ? "" : Line.IsForward ? Tones.Green : Line.IsBlock ? Tones.Red : Tones.Accent;

        public string TargetText
        {
            get
            {
                if (Line.Kind == HostsLineKind.Include) return Line.Target;

                return Line.IsMapping ? Line.Target : "";
            }
        }

        public string HostsText
        {
            get
            {
                if (Line.Kind == HostsLineKind.Rule) return string.Join("   ", Line.Hosts);

                return Line.Kind == HostsLineKind.Comment ? Line.Comment : "";
            }
        }

        public string HostsToolTip => Line.Kind == HostsLineKind.Rule && Line.Hosts.Length > 1 ? string.Join("\r\n", Line.Hosts.Take(40)) + (Line.Hosts.Length > 40 ? "\r\n..." : "") : null;

        public string CommentText => IsEntry ? Line.Comment : "";

        public string Problem => Line.Problem ?? ExtraProblem;

        public bool HasProblem => Problem != null;

        public override string ToString() { return Line.Raw; }
    }

    /// <summary>
    /// The page that edits AcrylicHosts.txt, either entry by entry or as plain text.
    /// </summary>
    public partial class HostsView : UserControl, IEditorPage
    {
        private IShell shell;

        private TextFile file = new TextFile();
        private HostsDocument document = HostsDocument.Parse("");
        private HostsMatcher matcher;

        private string savedText = "";

        private List<HostsRow> allRows = new List<HostsRow>();
        private HostsMatch shownMatch;

        private bool building;
        private bool textMode;
        private bool dirty;

        public HostsView()
        {
            InitializeComponent();

            TableLayout.FillColumn(EntryList, 3, 220);
        }

        public void Initialize(IShell shell, UiSettings settings)
        {
            this.shell = shell;

            ShowCommentsBox.IsChecked = settings.ShowHostsComments;

            LoadFromDisk();
        }

        public bool ShowComments => ShowCommentsBox.IsChecked == true;

        // ------------------------------------------------------------------------------------------------------------
        // IEditorPage
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>The plain text editor of the page.</summary>
        public TextBox Editor => TextEditor;

        public string FileName => "AcrylicHosts.txt";

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
            string failure = null;

            try
            {
                file = File.Exists(AcrylicPaths.HostsFile) ? TextFile.Load(AcrylicPaths.HostsFile) : new TextFile();

                if (!File.Exists(AcrylicPaths.HostsFile)) failure = "AcrylicHosts.txt was not found in \"" + AcrylicPaths.Directory + "\". It will be created when you save.";
            }
            catch (Exception e)
            {
                file = new TextFile();

                failure = "AcrylicHosts.txt could not be read: " + e.Message;
            }

            savedText = file.Text;

            document = HostsDocument.Parse(savedText);

            building = true;

            // The text editor holds the file only while it is shown: a hosts file can be very large.

            TextEditor.Text = textMode ? savedText : "";
            ProblemsOnlyButton.IsChecked = false;

            building = false;

            if (textMode) NoticePanel.Visibility = Visibility.Collapsed;

            RebuildRows(null);

            UpdateDirty();

            if (failure != null) shell.SetStatus(failure);
        }

        // ------------------------------------------------------------------------------------------------------------
        // The table
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the table from the document, selecting the lines having the specified indexes.
        /// </summary>
        private void RebuildRows(ICollection<int> selectIndexes)
        {
            matcher = null;

            allRows = new List<HostsRow>(document.Lines.Count);

            for (int i = 0; i < document.Lines.Count; i++)
            {
                HostsLine line = document.Lines[i];

                var row = new HostsRow(this, line, i);

                if (line.Kind == HostsLineKind.Include && !line.Disabled && line.Problem == null && !File.Exists(HostsMatcher.MakeAbsolutePath(AcrylicPaths.Directory, line.Target)))
                {
                    row.ExtraProblem = "The file \"" + line.Target.Trim() + "\" does not exist, so Acrylic skips this line.";
                }

                allRows.Add(row);
            }

            ApplyFilter(selectIndexes);

            UpdateSummary();
            UpdateTryResult();
        }

        private void ApplyFilter(ICollection<int> selectIndexes)
        {
            string[] words = SearchBox.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            bool showComments = ShowComments;
            bool problemsOnly = ProblemsOnlyButton.IsChecked == true;

            var shown = new List<HostsRow>();

            foreach (HostsRow row in allRows)
            {
                if (problemsOnly)
                {
                    if (!row.HasProblem) continue;
                }
                else if (!row.IsEntry && !showComments)
                {
                    continue;
                }

                bool matches = true;

                foreach (string word in words)
                {
                    if (row.Line.Raw.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) { matches = false; break; }
                }

                if (matches) shown.Add(row);
            }

            building = true;

            EntryList.ItemsSource = shown;

            if (selectIndexes != null && selectIndexes.Count > 0)
            {
                HostsRow first = null;

                foreach (HostsRow row in shown)
                {
                    if (!selectIndexes.Contains(row.Index)) continue;

                    EntryList.SelectedItems.Add(row);

                    if (first == null) first = row;
                }

                if (first != null) EntryList.ScrollIntoView(first);
            }

            building = false;

            if (shown.Count == 0)
            {
                EmptyText.Text = problemsOnly ? "No line has a problem."
                    : words.Length > 0 ? "Nothing matches \"" + SearchBox.Text.Trim() + "\"." + (showComments ? "" : "\r\nComments are hidden: tick \"Show comments and blank lines\" to search them too.")
                    : "The hosts file has no entries yet.\r\nUse \"Add an entry\" to map a domain name to an address, to block it or to forward it.";

                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyText.Visibility = Visibility.Collapsed;
            }

            UpdateButtons();
        }

        private List<HostsRow> SelectedRows => EntryList.SelectedItems.Cast<HostsRow>().OrderBy(r => r.Index).ToList();

        private void UpdateButtons()
        {
            List<HostsRow> selected = SelectedRows;

            EditButton.IsEnabled = selected.Count == 1 && selected[0].Line.Kind != HostsLineKind.Blank;
            DeleteButton.IsEnabled = selected.Count > 0;
            SwitchButton.IsEnabled = selected.Any(r => r.IsEntry);
            MoveUpButton.IsEnabled = selected.Count == 1 && selected[0].Index > 0;
            MoveDownButton.IsEnabled = selected.Count == 1 && selected[0].Index < document.Lines.Count - 1;
        }

        private void UpdateSummary()
        {
            int answers = 0, blocks = 0, forwards = 0, includes = 0, off = 0, names = 0, patterns = 0, expressions = 0, problems = 0;

            foreach (HostsRow row in allRows)
            {
                HostsLine line = row.Line;

                if (row.HasProblem) problems++;

                if (!row.IsEntry) continue;

                if (line.Disabled) { off++; continue; }

                if (line.Kind == HostsLineKind.Include) { includes++; continue; }

                if (line.IsForward) forwards++; else if (line.IsBlock) blocks++; else answers++;

                foreach (string host in line.Hosts)
                {
                    if (host.Length > 0 && host[0] == '/') expressions++;
                    else if (host.Length > 0 && host[0] == '>' || host.IndexOf('*') >= 0 || host.IndexOf('?') >= 0) patterns++;
                    else names++;
                }
            }

            var parts = new List<string>
            {
                Count(answers + blocks + forwards, "entry", "entries") + " in use (" + answers + " answered with an address, " + blocks + " blocked, " + forwards + " forwarded)",
                Count(names, "domain name", "domain names") + ", " + Count(patterns, "pattern", "patterns") + ", " + Count(expressions, "regular expression", "regular expressions")
            };

            if (includes > 0) parts.Add(Count(includes, "included file", "included files"));
            if (off > 0) parts.Add(off + " switched off");

            parts.Add(Count(allRows.Count, "line", "lines"));

            SummaryText.Text = string.Join("   \u00B7   ", parts);

            if (problems > 0 && !textMode)
            {
                NoticeText.Text = (problems == 1 ? "1 line has a problem" : problems + " lines have a problem") + " and may not work the way you expect. Hover the warning sign of a line to see why.";
                NoticePanel.Visibility = Visibility.Visible;
            }
            else
            {
                NoticePanel.Visibility = Visibility.Collapsed;

                if (problems == 0 && ProblemsOnlyButton.IsChecked == true)
                {
                    building = true; ProblemsOnlyButton.IsChecked = false; building = false;

                    ApplyFilter(null);
                }
            }
        }

        private static string Count(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!building) ApplyFilter(null);
        }

        private void ShowComments_Changed(object sender, RoutedEventArgs e)
        {
            if (shell != null && !building) ApplyFilter(SelectedRows.Select(r => r.Index).ToList());
        }

        private void ProblemsOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (shell != null && !building) ApplyFilter(null);
        }

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!building) UpdateButtons();
        }

        private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // A double click on the check box of a row must not open the editor.

            if (e.OriginalSource is DependencyObject source && FindAncestor<CheckBox>(source) == null && FindAncestor<ListViewItem>(source) != null) EditSelected();
        }

        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null && !(element is T)) element = (element is Visual || element is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

            return element as T;
        }

        private void EntryList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            switch (e.Key)
            {
                case Key.Enter: EditSelected(); e.Handled = true; break;
                case Key.Delete: DeleteSelected(); e.Handled = true; break;
                case Key.Insert: AddEntry(); e.Handled = true; break;
                case Key.Space: ToggleSelected(); e.Handled = true; break;
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------------------------------------------------

        private void OnEdited(ICollection<int> selectIndexes)
        {
            RebuildRows(selectIndexes);

            UpdateDirty();
        }

        private void Add_Click(object sender, RoutedEventArgs e) { AddEntry(); }
        private void Edit_Click(object sender, RoutedEventArgs e) { EditSelected(); }
        private void Delete_Click(object sender, RoutedEventArgs e) { DeleteSelected(); }
        private void Toggle_Click(object sender, RoutedEventArgs e) { ToggleSelected(); }
        private void MoveUp_Click(object sender, RoutedEventArgs e) { MoveSelected(-1); }
        private void MoveDown_Click(object sender, RoutedEventArgs e) { MoveSelected(+1); }

        private void AddEntry()
        {
            var dialog = new HostsEntryDialog(null) { Owner = Window.GetWindow(this) };

            if (dialog.ShowDialog() != true) return;

            List<HostsRow> selected = SelectedRows;

            // After the selected line, or at the end of the file.

            int index = selected.Count > 0 ? selected[selected.Count - 1].Index + 1 : document.Lines.Count;

            document.Lines.Insert(index, dialog.Result);

            ClearFilterIfHiding(dialog.Result);

            OnEdited(new[] { index });

            shell.SetStatus("Entry added. Nothing is written to AcrylicHosts.txt until you save.");

            EntryList.Focus();
        }

        private void EditSelected()
        {
            List<HostsRow> selected = SelectedRows;

            if (selected.Count != 1 || selected[0].Line.Kind == HostsLineKind.Blank) return;

            var dialog = new HostsEntryDialog(selected[0].Line) { Owner = Window.GetWindow(this) };

            if (dialog.ShowDialog() != true || dialog.Result.Raw == selected[0].Line.Raw) return;

            dialog.Result.Terminator = selected[0].Line.Terminator;

            document.Lines[selected[0].Index] = dialog.Result;

            ClearFilterIfHiding(dialog.Result);

            OnEdited(new[] { selected[0].Index });

            shell.SetStatus("Line changed. Nothing is written to AcrylicHosts.txt until you save.");

            EntryList.Focus();
        }

        /// <summary>
        /// Makes sure the line just added or changed does not vanish from sight because of the filter.
        /// </summary>
        private void ClearFilterIfHiding(HostsLine line)
        {
            building = true;

            foreach (string word in SearchBox.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Raw.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) { SearchBox.Text = ""; break; }
            }

            if (line.Kind == HostsLineKind.Comment || line.Kind == HostsLineKind.Blank) ShowCommentsBox.IsChecked = true;

            ProblemsOnlyButton.IsChecked = false;

            building = false;
        }

        private void DeleteSelected()
        {
            List<HostsRow> selected = SelectedRows;

            if (selected.Count == 0) return;

            for (int i = selected.Count - 1; i >= 0; i--) document.Lines.RemoveAt(selected[i].Index);

            OnEdited(null);

            shell.SetStatus((selected.Count == 1 ? "1 line" : selected.Count + " lines") + " deleted. Nothing is written to AcrylicHosts.txt until you save.");
        }

        private void ToggleSelected()
        {
            List<HostsRow> entries = SelectedRows.Where(r => r.IsEntry).ToList();

            if (entries.Count == 0) return;

            // If anything selected is on, everything goes off; otherwise everything goes on.

            SetEnabled(entries, !entries.Any(r => r.Enabled));
        }

        public void SetEnabled(IList<HostsRow> entries, bool enabled)
        {
            List<int> selection = SelectedRows.Select(r => r.Index).ToList();

            foreach (HostsRow row in entries) document.Lines[row.Index] = row.Line.WithDisabled(!enabled);

            // The table is rebuilt once the click that triggered this has been fully handled.

            Dispatcher.BeginInvoke(new Action(() => OnEdited(selection)));
        }

        private void MoveSelected(int direction)
        {
            List<HostsRow> selected = SelectedRows;

            if (selected.Count != 1) return;

            // The line trades places with the nearest line shown in the table, hopping over the hidden ones.

            var shown = (List<HostsRow>)EntryList.ItemsSource;

            int position = shown.IndexOf(selected[0]) + direction;

            int from = selected[0].Index;
            int to = (position >= 0 && position < shown.Count) ? shown[position].Index : from + direction;

            if (to < 0 || to >= document.Lines.Count) return;

            HostsLine line = document.Lines[from];

            document.Lines.RemoveAt(from);
            document.Lines.Insert(to, line);

            OnEdited(new[] { to });

            EntryList.Focus();
        }

        // ------------------------------------------------------------------------------------------------------------
        // Trying a domain name
        // ------------------------------------------------------------------------------------------------------------

        private void TryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTryResult();
        }

        private void UpdateTryResult()
        {
            if (TryResult == null) return;

            string domainName = AddressCacheFile.NormalizeDomainName(TryBox.Text);

            shownMatch = null;

            TryShowButton.Visibility = Visibility.Collapsed;

            if (domainName.Length == 0)
            {
                TryResult.Text = "Type a domain name to see what the hosts file does with it.";
                Tones.Apply(TryResult, null);

                return;
            }

            if (matcher == null) matcher = HostsMatcher.Build(document, AcrylicPaths.Directory);

            HostsVerdict verdict = matcher.Resolve(domainName);

            HostsMatch match = verdict.Deciding;

            string text = verdict.Describe() + ".";

            if (match != null)
            {
                text += "  Decided by \"" + match.Entry + "\"" + (match.SourceFile != null ? " in " + match.SourceFile : "") + ", line " + (match.LineIndex + 1) + ".";

                if (match.SourceFile == null)
                {
                    shownMatch = match;

                    TryShowButton.Visibility = Visibility.Visible;
                }
            }

            TryResult.Text = text;
            Tones.Apply(TryResult, verdict.Forward != null ? Tones.Green : verdict.Block != null ? Tones.Red : verdict.Handled ? Tones.Accent : null);
        }

        private void TryShow_Click(object sender, RoutedEventArgs e)
        {
            if (shownMatch == null) return;

            building = true;

            SearchBox.Text = "";
            ProblemsOnlyButton.IsChecked = false;

            building = false;

            ApplyFilter(new[] { shownMatch.LineIndex });

            EntryList.Focus();
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
                textMode = true;
                building = true;

                TextEditor.Text = document.ToText();

                building = false;

                StructuredPanel.Visibility = Visibility.Collapsed;
                NoticePanel.Visibility = Visibility.Collapsed;
                TextPanel.Visibility = Visibility.Visible;
                SearchBox.IsEnabled = false;

                TextEditor.Focus();
            }
            else
            {
                document = HostsDocument.Parse(TextEditor.Text);

                textMode = false;
                building = true;

                TextEditor.Text = "";

                building = false;

                TextPanel.Visibility = Visibility.Collapsed;
                StructuredPanel.Visibility = Visibility.Visible;
                SearchBox.IsEnabled = true;

                RebuildRows(null);
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

        private string CurrentText => textMode ? TextEditor.Text : document.ToText();

        private void UpdateDirty()
        {
            bool isDirty = CurrentText != savedText;

            UnsavedChip.Visibility = isDirty ? Visibility.Visible : Visibility.Collapsed;

            SaveButton.IsEnabled = isDirty;
            DiscardButton.IsEnabled = isDirty;

            if (isDirty != dirty)
            {
                dirty = isDirty;

                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        private void Discard_Click(object sender, RoutedEventArgs e)
        {
            if (!dirty) return;

            if (MessageDialog.Show(Window.GetWindow(this), "Discard every change made to AcrylicHosts.txt since the last save?", App.Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;

            LoadFromDisk();

            shell.SetStatus("The changes made to AcrylicHosts.txt have been discarded.");
        }

        public bool Save()
        {
            if (!dirty) return true;

            string text = CurrentText;

            int problems = textMode ? HostsDocument.Parse(text).Lines.Count(l => l.Problem != null) : allRows.Count(r => r.HasProblem);

            if (problems > 0)
            {
                // Acrylic itself does not refuse such lines, it just does not do what was meant, so saving stays possible.

                string question = (problems == 1 ? "1 line has a problem" : problems + " lines have a problem") + " and may not work the way you expect.\r\n\r\nSave anyway?";

                if (MessageDialog.Show(Window.GetWindow(this), question, App.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            }

            try
            {
                shell.SetStatus("Saving changes to file \"" + AcrylicPaths.HostsFile + "\"...");

                file.Text = text;
                file.Save(AcrylicPaths.HostsFile);
            }
            catch (Exception e)
            {
                shell.SetStatus("An error occurred while saving changes to file \"" + AcrylicPaths.HostsFile + "\".");

                MessageDialog.Show(Window.GetWindow(this), "AcrylicHosts.txt could not be saved.\r\n\r\n" + e.Message, App.Title, MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }

            savedText = text;

            UpdateDirty();

            shell.SetStatus("Changes saved to file \"" + AcrylicPaths.HostsFile + "\" successfully.");
            shell.NotifyFileSaved(FileName);

            return true;
        }
    }
}
