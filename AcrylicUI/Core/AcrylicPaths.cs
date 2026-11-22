using System;
using System.IO;
using System.Reflection;

namespace AcrylicUI.Core
{
    /// <summary>
    /// The files the UI works with. Like the Acrylic service and console, the UI always refers to the files found in
    /// the directory of its own executable.
    /// </summary>
    public static class AcrylicPaths
    {
        public const string ServiceName = "AcrylicDNSProxySvc";

        public const string HomePage = "https://mayakron.altervista.org/support/acrylic/Home.htm";

        /// <summary>
        /// When a file with this name sits next to AcrylicUI.exe the UI works in sandbox mode: instead of the Acrylic
        /// Windows service it drives a private AcrylicConsole.exe process, and it never touches the service.
        /// </summary>
        public const string SandboxMarkerFileName = "AcrylicSandbox.txt";

        public static readonly string Directory = AppDomain.CurrentDomain.BaseDirectory;

        public static readonly string ConfigurationFile = Path.Combine(Directory, "AcrylicConfiguration.ini");
        public static readonly string HostsFile = Path.Combine(Directory, "AcrylicHosts.txt");
        public static readonly string CacheFile = Path.Combine(Directory, "AcrylicCache.dat");
        public static readonly string DebugLogFile = Path.Combine(Directory, "AcrylicDebug.txt");
        /// <summary>
        /// The settings of the UI are kept next to its executable and named after it: AcrylicUI.ini for AcrylicUI.exe.
        /// A copy of the UI started under another name (to try a new version next to the installed one, for example)
        /// therefore keeps its settings apart.
        /// </summary>
        public static readonly string UiSettingsFile = Path.Combine(Directory, ExecutableName + ".ini");
        public static readonly string ServiceExecutable = Path.Combine(Directory, "AcrylicService.exe");
        public static readonly string ConsoleExecutable = Path.Combine(Directory, "AcrylicConsole.exe");
        public static readonly string SandboxMarkerFile = Path.Combine(Directory, SandboxMarkerFileName);

        public static bool IsSandbox => File.Exists(SandboxMarkerFile);

        private static string ExecutableName
        {
            get
            {
                string location = Assembly.GetEntryAssembly()?.Location;

                return string.IsNullOrEmpty(location) ? "AcrylicUI" : Path.GetFileNameWithoutExtension(location);
            }
        }

        public static string VersionNumber
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;

                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }

        public static string ReleaseDate
        {
            get
            {
                foreach (AssemblyMetadataAttribute attribute in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (attribute.Key == "AcrylicReleaseDate") return attribute.Value;
                }

                return "";
            }
        }
    }
}
