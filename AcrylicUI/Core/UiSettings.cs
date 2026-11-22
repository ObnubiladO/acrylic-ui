using System;
using System.Globalization;
using System.IO;

namespace AcrylicUI.Core
{
    /// <summary>
    /// The settings of the UI itself, kept in AcrylicUI.ini like the previous versions did.
    /// </summary>
    public sealed class UiSettings
    {
        private const string Section = "MainForm";

        public bool Maximized;
        public double Left = double.NaN, Top = double.NaN, Width = 1120, Height = 760;
        public int SelectedTab;
        public bool ShowHostsComments;
        public double EditorFontSize = 13;

        /// <summary>Light, Dark or System.</summary>
        public string Theme = "Light";

        public static UiSettings Load()
        {
            var settings = new UiSettings();

            try
            {
                if (!File.Exists(AcrylicPaths.UiSettingsFile)) return settings;

                IniDocument document = IniDocument.Parse(File.ReadAllText(AcrylicPaths.UiSettingsFile));

                settings.Maximized = ReadNumber(document, "WindowState", 0) == 2; // The value Delphi used for a maximized window.
                settings.Left = ReadNumber(document, "Left", double.NaN);
                settings.Top = ReadNumber(document, "Top", double.NaN);
                settings.Width = ReadNumber(document, "Width", settings.Width);
                settings.Height = ReadNumber(document, "Height", settings.Height);
                settings.SelectedTab = (int)ReadNumber(document, "SelectedTab", 0);
                settings.ShowHostsComments = ReadNumber(document, "ShowHostsComments", 0) != 0;
                settings.EditorFontSize = Math.Max(9, Math.Min(28, ReadNumber(document, "EditorFontSize", settings.EditorFontSize)));
                settings.Theme = document.Get(Section, "Theme") ?? settings.Theme;
            }
            catch (Exception)
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                IniDocument document = File.Exists(AcrylicPaths.UiSettingsFile) ? IniDocument.Parse(File.ReadAllText(AcrylicPaths.UiSettingsFile)) : IniDocument.Parse("[" + Section + "]\r\n");

                document.Set(Section, "WindowState", Maximized ? "2" : "0");

                if (!double.IsNaN(Left) && !double.IsNaN(Top))
                {
                    document.Set(Section, "Left", Format(Left));
                    document.Set(Section, "Top", Format(Top));
                }

                document.Set(Section, "Width", Format(Width));
                document.Set(Section, "Height", Format(Height));
                document.Set(Section, "SelectedTab", SelectedTab.ToString(CultureInfo.InvariantCulture));
                document.Set(Section, "ShowHostsComments", ShowHostsComments ? "1" : "0");
                document.Set(Section, "EditorFontSize", Format(EditorFontSize));
                document.Set(Section, "Theme", Theme);

                File.WriteAllText(AcrylicPaths.UiSettingsFile, document.ToText());
            }
            catch (Exception)
            {
            }
        }

        private static string Format(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        private static double ReadNumber(IniDocument document, string key, double fallback)
        {
            return double.TryParse(document.Get(Section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
        }
    }
}
