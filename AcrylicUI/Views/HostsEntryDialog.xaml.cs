using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcrylicUI.Core;
using Microsoft.Win32;

namespace AcrylicUI.Views
{
    /// <summary>
    /// Adds or edits one line of the hosts file, showing the resulting line (or what is wrong with it) while typing.
    /// </summary>
    public partial class HostsEntryDialog : Window
    {
        private readonly bool commentOnly;
        private bool namesConverted;
        private bool ready;

        /// <summary>The line to write to the file, once the dialog has been confirmed.</summary>
        public HostsLine Result { get; private set; }

        public HostsEntryDialog(HostsLine existing)
        {
            InitializeComponent();

            ThemeManager.Attach(this);

            if (existing == null)
            {
                AnswerChoice.IsChecked = true;
            }
            else
            {
                Title = "Edit the line";
                OkButton.Content = "OK";

                if (existing.Kind == HostsLineKind.Comment)
                {
                    commentOnly = true;

                    CommentOnlyBox.Text = existing.Comment ?? "";

                    KindPanel.Visibility = AddressPanel.Visibility = HostsPanel.Visibility = CommentPanel.Visibility = EnabledBox.Visibility = Visibility.Collapsed;
                    CommentOnlyPanel.Visibility = Visibility.Visible;
                }
                else if (existing.Kind == HostsLineKind.Include)
                {
                    IncludeChoice.IsChecked = true;

                    FileBox.Text = existing.Target.Trim();
                }
                else
                {
                    if (existing.IsForward) ForwardChoice.IsChecked = true; else if (existing.IsBlock) BlockChoice.IsChecked = true; else { AnswerChoice.IsChecked = true; AddressBox.Text = existing.Target; }

                    HostsBox.Text = string.Join("\r\n", existing.Hosts);
                    CommentBox.Text = existing.Comment ?? "";
                }

                EnabledBox.IsChecked = !existing.Disabled;
            }

            ready = true;

            UpdateLayoutForKind();
            Evaluate();

            Loaded += (sender, e) =>
            {
                if (commentOnly) CommentOnlyBox.Focus(); else if (IncludeChoice.IsChecked == true) FileBox.Focus(); else if (AnswerChoice.IsChecked == true && AddressBox.Text.Length == 0) AddressBox.Focus(); else HostsBox.Focus();
            };
        }

        private void Kind_Changed(object sender, RoutedEventArgs e)
        {
            if (!ready) return;

            UpdateLayoutForKind();
            Evaluate();
        }

        private void UpdateLayoutForKind()
        {
            if (commentOnly) return;

            bool include = IncludeChoice.IsChecked == true;

            AddressPanel.Visibility = AnswerChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            HostsPanel.Visibility = include ? Visibility.Collapsed : Visibility.Visible;
            CommentPanel.Visibility = include ? Visibility.Collapsed : Visibility.Visible;
            FilePanel.Visibility = include ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            if (ready) Evaluate();
        }

        private void QuickAddress_Click(object sender, RoutedEventArgs e)
        {
            AddressBox.Text = (string)((Button)sender).Content;

            HostsBox.Focus();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Hosts file to include", Filter = "Text files|*.txt|All files|*.*", InitialDirectory = AcrylicPaths.Directory, CheckFileExists = false };

            if (dialog.ShowDialog(this) != true) return;

            string directory = Path.GetDirectoryName(dialog.FileName) ?? "";

            // A file living in the Acrylic folder is referred to by its name alone, which keeps the folder movable.

            FileBox.Text = string.Equals(directory.TrimEnd('\\'), AcrylicPaths.Directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(dialog.FileName) : dialog.FileName;
        }

        /// <summary>
        /// Builds the line from what has been typed. Returns what is missing or wrong, or null.
        /// </summary>
        private string Compose(out HostsLine line)
        {
            line = null;

            namesConverted = false;

            if (commentOnly)
            {
                string text = CommentOnlyBox.Text.Replace("\r", " ").Replace("\n", " ");

                line = HostsLine.CreateComment(text);

                return null;
            }

            bool disabled = EnabledBox.IsChecked != true;

            if (IncludeChoice.IsChecked == true)
            {
                string fileName = FileBox.Text.Trim();

                if (fileName.Length == 0) return "Type the name of the file to include.";

                line = HostsLine.CreateInclude(fileName, disabled);

                return line.Problem;
            }

            string target = ForwardChoice.IsChecked == true ? HostsLine.Forward : BlockChoice.IsChecked == true ? HostsLine.Block : AddressBox.Text.Trim();

            if (target.Length == 0) return "Type the IP address to answer with.";

            if (target != HostsLine.Forward && target != HostsLine.Block && !ConfigSchema.IsIPv4(target) && !ConfigSchema.IsIPv6(target)) return "\"" + target + "\" is not an IPv4 or IPv6 address.";

            string[] hosts = HostsBox.Text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < hosts.Length; i++)
            {
                string ascii = DomainNames.ToAsciiHostsEntry(hosts[i]);

                if (ascii != hosts[i]) { hosts[i] = ascii; namesConverted = true; }
            }

            if (hosts.Length == 0) return "Type at least one domain name.";

            foreach (string host in hosts)
            {
                string problem = HostsLine.ValidateHost(host);

                if (problem != null) return problem;
            }

            string comment = CommentBox.Text.Replace("\r", " ").Replace("\n", " ").Trim();

            line = HostsLine.CreateRule(target, hosts, comment, disabled);

            if (line.Kind != HostsLineKind.Rule || line.Disabled != disabled || !line.Hosts.SequenceEqual(hosts)) return "This entry cannot be written the way it has been typed.";

            return line.Problem;
        }

        private void Evaluate()
        {
            string problem = Compose(out HostsLine line);

            if (problem != null)
            {
                PreviewCaption.Text = "NOT READY YET";
                PreviewText.Text = problem;
                PreviewText.FontFamily = FontFamily;

                Tones.Apply(PreviewPanel, PreviewText, Tones.Amber);
            }
            else
            {
                PreviewCaption.Text = namesConverted ? "LINE WRITTEN TO THE FILE  -  names with accents are written in their xn-- form, the one DNS requests carry" : "LINE WRITTEN TO THE FILE";
                PreviewText.Text = line.Raw.Length == 0 ? " " : line.Raw;
                PreviewText.FontFamily = (FontFamily)FindResource("MonoFont");

                Tones.Apply(PreviewPanel, PreviewText, Tones.Plain);
            }

            OkButton.IsEnabled = (problem == null);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Compose(out HostsLine line) != null) return;

            Result = line;

            DialogResult = true;
        }
    }
}
