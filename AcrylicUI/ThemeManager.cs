using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;

namespace AcrylicUI
{
    public enum ThemeChoice
    {
        Light,
        Dark,

        /// <summary>Light or dark according to the "app mode" chosen in the Windows settings.</summary>
        System
    }

    /// <summary>
    /// Switches the whole application between its light and its dark appearance, at any time.
    ///
    /// The two appearances are the resource dictionaries Themes\Light.xaml and Themes\Dark.xaml, which define the same
    /// set of brushes. Everything else refers to those brushes as dynamic resources, so replacing one dictionary with
    /// the other repaints every window that is open.
    /// </summary>
    public static class ThemeManager
    {
        private static bool applied;

        public static ThemeChoice Choice { get; private set; } = ThemeChoice.Light;

        public static bool IsDark { get; private set; }

        /// <summary>Raised after the choice, or the appearance resulting from it, has changed.</summary>
        public static event EventHandler Changed;

        public static ThemeChoice Parse(string text)
        {
            return Enum.TryParse(text, true, out ThemeChoice choice) ? choice : ThemeChoice.Light;
        }

        public static void Apply(ThemeChoice choice)
        {
            bool dark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && WindowsUsesDarkApps());

            Choice = choice;

            if (!applied || dark != IsDark)
            {
                IsDark = dark;
                applied = true;

                // App.xaml lists the palette first, before the styles that use it.

                Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/AcrylicUI;component/Themes/" + (dark ? "Dark" : "Light") + ".xaml", UriKind.Relative) };

                foreach (Window window in Application.Current.Windows) ApplyToTitleBar(window);
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// To be called when Windows says that its colours have changed.
        /// </summary>
        public static void FollowWindows()
        {
            if (Choice == ThemeChoice.System) Apply(ThemeChoice.System);
        }

        private static bool WindowsUsesDarkApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Makes the title bar of a window, which is drawn by Windows, follow the appearance of the application.
        /// </summary>
        public static void Attach(Window window)
        {
            window.SourceInitialized += (sender, e) => ApplyToTitleBar(window);
        }

        private static void ApplyToTitleBar(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero) return;

            try
            {
                int dark = IsDark ? 1 : 0;

                // The attribute has number 20 since Windows 10 version 2004, and had number 19 in the versions before.
                // Windows versions that know neither just keep their usual title bar.

                if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));

                // Windows 10 repaints the title bar only when the frame of the window changes.

                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch (Exception)
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }

    /// <summary>
    /// The colours that tell something (success, failure, warning...), set from code in a way that follows the
    /// appearance of the application when it changes.
    /// </summary>
    public static class Tones
    {
        public const string Green = "Green", Red = "Red", Amber = "Amber", Accent = "Accent";

        /// <summary>Ordinary text on a neutral background.</summary>
        public const string Plain = "Plain";

        private static string BackgroundKey(string tone)
        {
            return string.IsNullOrEmpty(tone) || tone == Plain ? "GraySoftBrush" : tone + "SoftBrush";
        }

        private static string ForegroundKey(string tone)
        {
            if (string.IsNullOrEmpty(tone)) return "SubtleBrush";

            return tone == Plain ? "TextBrush" : tone == Accent ? "AccentTextBrush" : tone + "Brush";
        }

        public static void Apply(TextBlock text, string tone)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, ForegroundKey(tone));
        }

        public static void Apply(Border panel, TextBlock text, string tone)
        {
            panel.SetResourceReference(Border.BackgroundProperty, BackgroundKey(tone));

            Apply(text, tone);
        }

        public static void Apply(Border panel, System.Windows.Shapes.Shape sign, TextBlock text, string tone)
        {
            Apply(panel, text, tone);

            sign.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ForegroundKey(tone));
        }
    }
}
