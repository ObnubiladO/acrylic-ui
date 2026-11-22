using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using AcrylicUI.Core;
using AcrylicUI.Views;

namespace AcrylicUI
{
    public enum StartupPage
    {
        Default,
        Configuration,
        Hosts,
        About
    }

    public partial class App : Application
    {
        public const string Title = "Acrylic DNS Proxy UI";

        /// <summary>
        /// The command line options that perform one maintenance operation and exit, without showing the window.
        /// Their names and their meaning are the ones of the previous versions of the UI, because the batch files
        /// distributed with Acrylic and its setup program rely on them.
        /// </summary>
        private static readonly string[] HeadlessOptions =
        {
            "InstallAcrylicService", "UninstallAcrylicService", "StartAcrylicService", "StopAcrylicService", "RestartAcrylicService",
            "PurgeAcrylicCacheData", "ActivateAcrylicDebugLog", "DeactivateAcrylicDebugLog"
        };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnUnhandledException;

            ThemeManager.Apply(ThemeManager.Parse(UiSettings.Load().Theme));

            string option = e.Args.Length > 0 ? e.Args[0] : "";

            bool sandbox = AcrylicPaths.IsSandbox;

            if (!sandbox && !IsElevated())
            {
                // Managing the Acrylic service and writing into its directory require administrative rights. Batch files
                // and setup programs running one of the headless options rely on the process they started to last as
                // long as the operation, so in that case this process waits for the elevated one.

                Shutdown(RelaunchElevated(e.Args, HeadlessOptions.Contains(option)));

                return;
            }

            if (HeadlessOptions.Contains(option))
            {
                Shutdown(RunHeadless(option, sandbox));

                return;
            }

            StartupPage page = StartupPage.Default;

            if (option == "OpenAcrylicConfigurationFile") page = StartupPage.Configuration;
            else if (option == "OpenAcrylicHostsFile") page = StartupPage.Hosts;
            else if (option == "About" || option == "Help" || option == "/?" || option == "--help") page = StartupPage.About;

            var window = new MainWindow(page);

            MainWindow = window;

            window.Closed += (sender, args) => Shutdown(0);

            window.Show();
        }

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static int RelaunchElevated(string[] args, bool wait)
        {
            var startInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location)
            {
                Arguments = string.Join(" ", args.Select(a => "\"" + a.Replace("\"", "\\\"") + "\"")),
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (!wait) return 0;

                    process.WaitForExit();

                    return process.ExitCode;
                }
            }
            catch (Win32Exception)
            {
                return 1; // The elevation request has been declined.
            }
        }

        private static int RunHeadless(string option, bool sandbox)
        {
            if (sandbox && (option.EndsWith("AcrylicService", StringComparison.Ordinal)))
            {
                MessageDialog.Show(null, "This copy of Acrylic works in sandbox mode (see " + AcrylicPaths.SandboxMarkerFileName + "): there is no Windows service to manage here.", Title, MessageBoxButton.OK, MessageBoxImage.Information);

                return 1;
            }

            try
            {
                using (AcrylicEngine engine = AcrylicEngine.Create())
                {
                    var operations = new AcrylicOperations(engine);

                    switch (option)
                    {
                        case "InstallAcrylicService": engine.Install(); engine.Start(); break;
                        case "UninstallAcrylicService": engine.Uninstall(); break;
                        case "StartAcrylicService": if (engine.GetState() != EngineState.Running) engine.Start(); break;
                        case "StopAcrylicService": if (engine.GetState() == EngineState.Running) engine.Stop(); break;
                        case "RestartAcrylicService": operations.Restart(null); break;
                        case "PurgeAcrylicCacheData": operations.PurgeCache(null); break;
                        case "ActivateAcrylicDebugLog": operations.SetDebugLogEnabled(true, null); break;
                        case "DeactivateAcrylicDebugLog": operations.SetDebugLogEnabled(false, null); break;
                    }
                }

                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            string message = "Something went wrong:\r\n\r\n" + e.Exception.Message;

            try
            {
                MessageDialog.Show(MainWindow, message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // Whatever went wrong may well prevent the application from drawing its own message box.

                MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
