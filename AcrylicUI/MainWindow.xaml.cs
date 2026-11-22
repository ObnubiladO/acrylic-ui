using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AcrylicUI.Core;
using AcrylicUI.Views;

namespace AcrylicUI
{
    public partial class MainWindow : Window, IShell
    {
        private readonly AcrylicEngine engine;
        private readonly AcrylicOperations operations;
        private readonly UiSettings settings;
        private readonly DispatcherTimer timer;
        private readonly StartupPage startupPage;

        private EngineState engineState = EngineState.Stopped;
        private bool engineStateKnown;
        private bool busy;

        public MainWindow(StartupPage startupPage)
        {
            this.startupPage = startupPage;

            InitializeComponent();

            ThemeManager.Attach(this);
            ThemeManager.Changed += (sender, e) => UpdateThemeButton();

            UpdateThemeButton();

            engine = AcrylicEngine.Create();
            operations = new AcrylicOperations(engine);
            settings = UiSettings.Load();

            VersionText.Text = "Version " + AcrylicPaths.VersionNumber + (engine.IsSandbox ? "  \u00B7  sandbox" : "");

            ApplyWindowPlacement();

            if (engine.IsSandbox)
            {
                // There is no service to install or uninstall in sandbox mode.

                MoreMenu.Items.Remove(InstallMenuItem);
                MoreMenu.Items.Remove(UninstallMenuItem);
                MoreMenu.Items.Remove(InstallSeparator);

                SandboxBanner.Visibility = Visibility.Visible;

                UpdateSandboxText();
            }

            ConfigurationPage.Initialize(this);
            HostsPage.Initialize(this, settings);
            CachePage.Initialize(this);

            ConfigurationPage.DirtyChanged += (sender, e) => UpdateTabHeaders();
            HostsPage.DirtyChanged += (sender, e) => UpdateTabHeaders();

            UpdateTabHeaders();

            foreach (TextBox editor in new[] { ConfigurationPage.Editor, HostsPage.Editor })
            {
                editor.FontSize = settings.EditorFontSize;
                editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
            }

            if (startupPage == StartupPage.Hosts) Tabs.SelectedItem = HostsTab;
            else if (startupPage == StartupPage.Configuration) Tabs.SelectedItem = ConfigurationTab;
            else if (settings.SelectedTab >= 0 && settings.SelectedTab < Tabs.Items.Count) Tabs.SelectedIndex = settings.SelectedTab;

            RefreshEngineState();

            SetStatus("Welcome to Acrylic version " + AcrylicPaths.VersionNumber + " released on " + AcrylicPaths.ReleaseDate + ".");

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (sender, e) => { if (!busy) RefreshEngineState(); };
            timer.Start();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (engine.IsSandbox && engineState == EngineState.Stopped)
            {
                // The sandboxed engine lives and dies with this window, so it is started right away.

                if (await RunAsync("The sandboxed Acrylic engine could not be started", progress => operations.Start(progress))) SetStatus("The sandboxed Acrylic engine has been started.");
            }

            if (startupPage == StartupPage.About) About_Click(this, null);
        }

        // ------------------------------------------------------------------------------------------------------------
        // IShell
        // ------------------------------------------------------------------------------------------------------------

        Window IShell.Window => this;

        public AcrylicOperations Operations => operations;

        public EngineState EngineState => engineState;

        public event EventHandler EngineStateChanged;

        public void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        public async Task<bool> RunAsync(string failureTitle, Action<Action<string>> work)
        {
            if (busy) return false;

            busy = true;

            BusyText.Text = "Working...";
            BusyOverlay.Visibility = Visibility.Visible;

            Action<string> progress = message => Dispatcher.BeginInvoke(new Action(() => { BusyText.Text = message; StatusText.Text = message; }));

            Exception failure = null;

            try
            {
                await Task.Run(() => work(progress));
            }
            catch (Exception e)
            {
                failure = e;
            }

            // Let the progress messages still in the queue go first, so they cannot overwrite the final status.

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

            BusyOverlay.Visibility = Visibility.Collapsed;

            busy = false;

            RefreshEngineState();

            if (failure != null)
            {
                SetStatus(failureTitle + ".");

                MessageDialog.Show(this, failureTitle + ".\r\n\r\n" + failure.Message, App.Title, MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }

            return true;
        }

        public void NotifyFileSaved(string fileName)
        {
            if (engine.IsSandbox) UpdateSandboxText();

            if (engineState == EngineState.Running)
            {
                RestartBannerText.Text = fileName + " has been saved. Acrylic reads it only when it starts: restart it to apply the changes.";
                RestartBanner.Visibility = Visibility.Visible;
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // State of the engine
        // ------------------------------------------------------------------------------------------------------------

        private void RefreshEngineState()
        {
            EngineState state;

            try
            {
                state = engine.GetState();
            }
            catch (Exception)
            {
                state = EngineState.Busy;
            }

            if (engineStateKnown && state == engineState) return;

            engineState = state;
            engineStateKnown = true;

            string text, tone, toolTip = null;

            switch (state)
            {
                case EngineState.Running:
                    text = "Running"; tone = Tones.Green;
                    break;

                case EngineState.Stopped:
                    text = "Stopped"; tone = Tones.Red;
                    break;

                case EngineState.Busy:
                    text = "Starting or stopping..."; tone = null;
                    break;

                case EngineState.Foreign:
                    text = "Service runs from another folder"; tone = Tones.Amber;
                    toolTip = "The Acrylic service installed on this computer is \"" + engine.ForeignExecutable + "\". This window edits the files found in \"" + AcrylicPaths.Directory + "\" instead, so it leaves that service alone. To manage that service, open the Acrylic UI located in its folder.";
                    break;

                default:
                    text = engine.IsSandbox ? "AcrylicConsole.exe is missing" : "Service not installed"; tone = Tones.Amber;
                    break;
            }

            Tones.Apply(StateChip, StateDot, StateText, tone);

            StateChip.ToolTip = toolTip;
            StateText.Text = text;

            bool controllable = (state == EngineState.Running || state == EngineState.Stopped);

            StartButton.Visibility = StopButton.Visibility = RestartButton.Visibility = controllable ? Visibility.Visible : Visibility.Collapsed;

            StartButton.IsEnabled = (state == EngineState.Stopped);
            StopButton.IsEnabled = (state == EngineState.Running);
            RestartButton.IsEnabled = (state == EngineState.Running);

            InstallButton.Visibility = (state == EngineState.NotInstalled && !engine.IsSandbox) ? Visibility.Visible : Visibility.Collapsed;

            if (state != EngineState.Running) RestartBanner.Visibility = Visibility.Collapsed;

            Title = "Acrylic DNS Proxy - " + text;

            EngineStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateSandboxText()
        {
            string where = "";

            try
            {
                IPEndPoint endPoint = AcrylicOperations.GetLocalEndPoint(AcrylicOperations.LoadConfiguration());

                if (endPoint != null) where = ", listening on " + endPoint;
            }
            catch (Exception)
            {
            }

            SandboxText.Text = "Sandbox mode: this window drives its own private Acrylic engine (AcrylicConsole.exe" + where + ") using the files in its own folder. The Acrylic service installed on this computer is never started, stopped or changed from here.";
        }

        private void UpdateTabHeaders()
        {
            ConfigurationTab.Header = ConfigurationPage.IsDirty ? "Configuration  \u25CF" : "Configuration";
            HostsTab.Header = HostsPage.IsDirty ? "Hosts  \u25CF" : "Hosts";
        }

        // ------------------------------------------------------------------------------------------------------------
        // Header actions
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Acrylic reads its files when it starts, so starting it while a page holds changes that are not saved yet
        /// is most likely a mistake: the user is asked what to do. Returns false if Acrylic must not be started.
        /// </summary>
        private bool SaveChangesBeforeStarting(string verb)
        {
            foreach (IEditorPage page in new IEditorPage[] { ConfigurationPage, HostsPage })
            {
                if (!page.IsDirty) continue;

                Tabs.SelectedItem = (page == ConfigurationPage) ? ConfigurationTab : HostsTab;

                MessageBoxResult answer = MessageDialog.Show(this, "The changes made to " + page.FileName + " are not saved yet, so Acrylic would " + verb + " without them.\r\n\r\nSave them first?", App.Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);

                if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !page.Save())) return false;
            }

            return true;
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveChangesBeforeStarting("start")) return;

            if (await RunAsync("An error occurred while starting Acrylic", progress => operations.Start(progress))) SetStatus("Acrylic has been started successfully.");
        }

        private async void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (await RunAsync("An error occurred while stopping Acrylic", progress => operations.Stop(progress))) SetStatus("Acrylic has been stopped successfully.");
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveChangesBeforeStarting("restart")) return;

            if (await RunAsync("An error occurred while restarting Acrylic", progress => operations.Restart(progress)))
            {
                RestartBanner.Visibility = Visibility.Collapsed;

                SetStatus("Acrylic has been restarted successfully.");
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Light and dark appearance
        // ------------------------------------------------------------------------------------------------------------

        private void UpdateThemeButton()
        {
            // The button shows what a click leads to: the moon while the appearance is light, the sun while it is dark.

            Look.SetGlyph(ThemeButton, ThemeManager.IsDark ? "\uE706" : "\uE708");

            ThemeButton.ToolTip = ThemeManager.IsDark ? "Switch to the light appearance" : "Switch to the dark appearance";

            System.Windows.Automation.AutomationProperties.SetName(ThemeButton, (string)ThemeButton.ToolTip);
        }

        private void SetTheme(ThemeChoice choice)
        {
            ThemeManager.Apply(choice);

            settings.Theme = choice.ToString();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            SetTheme(ThemeManager.IsDark ? ThemeChoice.Light : ThemeChoice.Dark);
        }

        private void ThemeLight_Click(object sender, RoutedEventArgs e) { SetTheme(ThemeChoice.Light); }

        private void ThemeDark_Click(object sender, RoutedEventArgs e) { SetTheme(ThemeChoice.Dark); }

        private void ThemeSystem_Click(object sender, RoutedEventArgs e) { SetTheme(ThemeChoice.System); }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Windows tells every window when its light or dark mode changes: that matters when the appearance of the
            // application is set to follow the one of Windows.

            (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.AddHook(OnWindowMessage);
        }

        private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SETTINGCHANGE = 0x001A;

            if (message == WM_SETTINGCHANGE && lParam != IntPtr.Zero && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            {
                Dispatcher.BeginInvoke(new Action(ThemeManager.FollowWindows));
            }

            return IntPtr.Zero;
        }

        private void RestartLater_Click(object sender, RoutedEventArgs e)
        {
            RestartBanner.Visibility = Visibility.Collapsed;
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            bool debugLog = operations.IsDebugLogEnabled;

            InstallMenuItem.IsEnabled = (engineState == EngineState.NotInstalled);
            UninstallMenuItem.IsEnabled = (engineState == EngineState.Running || engineState == EngineState.Stopped);
            PurgeMenuItem.IsEnabled = (engineState != EngineState.Busy);
            ActivateDebugLogMenuItem.IsEnabled = !debugLog;
            DeactivateDebugLogMenuItem.IsEnabled = debugLog;
            ViewDebugLogMenuItem.IsEnabled = debugLog;

            ThemeLightMenuItem.IsChecked = (ThemeManager.Choice == ThemeChoice.Light);
            ThemeDarkMenuItem.IsChecked = (ThemeManager.Choice == ThemeChoice.Dark);
            ThemeSystemMenuItem.IsChecked = (ThemeManager.Choice == ThemeChoice.System);

            MoreMenu.PlacementTarget = MoreButton;
            MoreMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            MoreMenu.HorizontalOffset = MoreButton.ActualWidth - 260;
            MoreMenu.IsOpen = true;
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (await RunAsync("An error occurred while installing the Acrylic service", progress =>
            {
                progress("Installing the Acrylic service...");

                engine.Install();

                operations.Start(progress);
            }))
            {
                SetStatus("The Acrylic service has been installed and started successfully.");
            }
        }

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (MessageDialog.Show(this, "Uninstall the Acrylic service?\r\n\r\nAcrylic stops answering DNS requests. If this computer uses Acrylic as its DNS server, name resolution stops working until you point it to another DNS server.", App.Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;

            if (await RunAsync("An error occurred while uninstalling the Acrylic service", progress =>
            {
                progress("Uninstalling the Acrylic service...");

                engine.Uninstall();
            }))
            {
                SetStatus("The Acrylic service has been uninstalled successfully.");
            }
        }

        private async void Purge_Click(object sender, RoutedEventArgs e)
        {
            await PurgeCacheAsync();
        }

        /// <summary>
        /// Empties the whole address cache, after asking for confirmation.
        /// </summary>
        public async Task<bool> PurgeCacheAsync()
        {
            string question = "Purge the whole Acrylic address cache?\r\n\r\nEvery cached response is forgotten" + (engineState == EngineState.Running ? " and Acrylic is restarted." : ".");

            if (MessageDialog.Show(this, question, App.Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return false;

            if (!await RunAsync("An error occurred while purging Acrylic cache data", progress => operations.PurgeCache(progress))) return false;

            SetStatus("Acrylic cache data has been purged successfully.");

            CachePage.Reload();

            return true;
        }

        private async void ActivateDebugLog_Click(object sender, RoutedEventArgs e)
        {
            if (await RunAsync("An error occurred while activating the Acrylic debug log", progress => operations.SetDebugLogEnabled(true, progress))) SetStatus("The Acrylic debug log has been activated successfully: " + AcrylicPaths.DebugLogFile);
        }

        private async void DeactivateDebugLog_Click(object sender, RoutedEventArgs e)
        {
            if (await RunAsync("An error occurred while deactivating the Acrylic debug log", progress => operations.SetDebugLogEnabled(false, progress))) SetStatus("The Acrylic debug log has been deactivated successfully.");
        }

        private void ViewDebugLog_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(AcrylicPaths.DebugLogFile)) Launch("notepad.exe", "\"" + AcrylicPaths.DebugLogFile + "\"");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Launch("explorer.exe", "\"" + AcrylicPaths.Directory.TrimEnd('\\') + "\"");
        }

        private void HomePage_Click(object sender, RoutedEventArgs e)
        {
            Launch(AcrylicPaths.HomePage, null);
        }

        private void Launch(string fileName, string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fileName, arguments ?? "") { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageDialog.Show(this, exception.Message, App.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegExTester_Click(object sender, RoutedEventArgs e)
        {
            new PatternTesterDialog(PatternTesterMode.RegularExpression) { Owner = this }.ShowDialog();
        }

        private void AffinityMaskTester_Click(object sender, RoutedEventArgs e)
        {
            new PatternTesterDialog(PatternTesterMode.AffinityMask) { Owner = this }.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageDialog.Show(this,
                "Acrylic is a local DNS proxy which improves the performance of your computer by caching the responses coming from your DNS servers and helps you fight unwanted ads through the use of a custom HOSTS file with support for wildcards and regular expressions.\r\n\r\n" +
                "For more information please use the \"Acrylic home page\" item of the menu, or go directly to:\r\n\r\n" + AcrylicPaths.HomePage + "\r\n\r\n" +
                "Installed version is:\r\n" + AcrylicPaths.VersionNumber + " released on " + AcrylicPaths.ReleaseDate + ".",
                "About Acrylic", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Window
        // ------------------------------------------------------------------------------------------------------------

        private IEditorPage CurrentEditorPage
        {
            get
            {
                if (Tabs.SelectedItem == ConfigurationTab) return ConfigurationPage;
                if (Tabs.SelectedItem == HostsTab) return HostsPage;

                return null;
            }
        }

        /// <summary>
        /// Ctrl + mouse wheel changes the text size of the plain text editors.
        /// </summary>
        private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            e.Handled = true;

            settings.EditorFontSize = Math.Max(9, Math.Min(28, settings.EditorFontSize + (e.Delta > 0 ? 1 : -1)));

            ConfigurationPage.Editor.FontSize = HostsPage.Editor.FontSize = settings.EditorFontSize;
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != Tabs) return;

            if (Tabs.SelectedItem == CacheTab && CachePage != null) CachePage.Reload();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (busy) { e.Handled = true; return; }

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.S)
                {
                    CurrentEditorPage?.Save();

                    e.Handled = true;
                }
                else if (e.Key == Key.F)
                {
                    if (Tabs.SelectedItem == CacheTab) CachePage.FocusSearch(); else CurrentEditorPage?.FocusSearch();

                    e.Handled = true;
                }
                else if (e.Key == Key.F1) { Tabs.SelectedItem = ConfigurationTab; e.Handled = true; }
                else if (e.Key == Key.F2) { Tabs.SelectedItem = HostsTab; e.Handled = true; }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (busy) { e.Cancel = true; return; }

            foreach (IEditorPage page in new IEditorPage[] { ConfigurationPage, HostsPage })
            {
                if (!page.IsDirty) continue;

                Tabs.SelectedItem = (page == ConfigurationPage) ? ConfigurationTab : HostsTab;

                MessageBoxResult answer = MessageDialog.Show(this, "Save the changes made to " + page.FileName + "?", App.Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);

                if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !page.Save())) { e.Cancel = true; return; }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();

            settings.Maximized = (WindowState == WindowState.Maximized);

            Rect bounds = RestoreBounds;

            if (!bounds.IsEmpty)
            {
                settings.Left = bounds.Left; settings.Top = bounds.Top; settings.Width = bounds.Width; settings.Height = bounds.Height;
            }

            settings.SelectedTab = Tabs.SelectedIndex;
            settings.ShowHostsComments = HostsPage.ShowComments;

            settings.Save();

            // In sandbox mode this stops the private engine, which saves its address cache on the way out.

            engine.Dispose();

            base.OnClosed(e);
        }

        private void ApplyWindowPlacement()
        {
            Width = Math.Max(MinWidth, Math.Min(settings.Width, SystemParameters.VirtualScreenWidth));
            Height = Math.Max(MinHeight, Math.Min(settings.Height, SystemParameters.VirtualScreenHeight));

            bool visible = !double.IsNaN(settings.Left) && !double.IsNaN(settings.Top)
                && settings.Left + 120 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth && settings.Left + Width - 120 > SystemParameters.VirtualScreenLeft
                && settings.Top + 40 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight && settings.Top >= SystemParameters.VirtualScreenTop - 10;

            if (visible)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;

                Left = settings.Left; Top = settings.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (settings.Maximized) WindowState = WindowState.Maximized;
        }
    }
}
