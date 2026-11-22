using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AcrylicUI.Core;

namespace AcrylicUI.Views
{
    public enum PatternTesterMode
    {
        RegularExpression,
        AffinityMask
    }

    /// <summary>
    /// Tells, while typing, whether a domain name matches a regular expression or a domain name affinity mask.
    /// </summary>
    public partial class PatternTesterDialog : Window
    {
        private readonly PatternTesterMode mode;

        public PatternTesterDialog(PatternTesterMode mode, string pattern = null)
        {
            this.mode = mode;

            InitializeComponent();

            ThemeManager.Attach(this);

            if (mode == PatternTesterMode.RegularExpression)
            {
                Title = "RegEx tester";
                IntroText.Text = "Checks a domain name against a regular expression the way the hosts file does: without regard to case, and without the leading '/' used in AcrylicHosts.txt.";
                PatternLabel.Text = "Regular expression";
                Look.SetPlaceholder(PatternBox, @"(?<!cdn\.)google\.com$");
            }
            else
            {
                Title = "Domain name affinity mask tester";
                IntroText.Text = "Checks a domain name against a domain name affinity mask: a semicolon separated list of names or wildcards, where a leading caret (^) negates. The first rule that matches decides.";
                PatternLabel.Text = "Affinity mask";
                Look.SetPlaceholder(PatternBox, "^*.com;^*.org;*");
            }

            PatternBox.Text = pattern ?? "";

            Loaded += (sender, e) => DomainBox.Focus();

            Evaluate();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsInitialized && ResultText != null) Evaluate();
        }

        private void Evaluate()
        {
            string domainName = DomainBox.Text.Trim();
            string pattern = PatternBox.Text.Trim();

            if (domainName.Length == 0 || (pattern.Length == 0 && mode == PatternTesterMode.RegularExpression))
            {
                Show("Type a domain name and " + (mode == PatternTesterMode.RegularExpression ? "a regular expression" : "an affinity mask") + " to see the result.", null);

                return;
            }

            bool matches;

            try
            {
                matches = (mode == PatternTesterMode.RegularExpression)
                    ? Regex.IsMatch(domainName, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2))
                    : PatternMatching.MatchAffinityMask(domainName, pattern);
            }
            catch (ArgumentException e)
            {
                Show("The regular expression is not valid: " + e.Message, Tones.Red);

                return;
            }
            catch (RegexMatchTimeoutException)
            {
                Show("The regular expression takes too long to evaluate.", Tones.Red);

                return;
            }

            if (matches) Show("\"" + domainName + "\" matches.", Tones.Green); else Show("\"" + domainName + "\" does not match.", Tones.Amber);
        }

        private void Show(string text, string tone)
        {
            ResultText.Text = text;
            Tones.Apply(ResultPanel, ResultText, tone);
        }
    }
}
