using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace AcrylicUI.Core
{
    /// <summary>
    /// The maintenance operations offered by the UI and by its command line. Each of them leaves Acrylic in the state
    /// it was found in: if Acrylic was running it is stopped for as short as possible and then started again.
    /// </summary>
    public sealed class AcrylicOperations
    {
        public AcrylicEngine Engine { get; }

        public AcrylicOperations(AcrylicEngine engine)
        {
            Engine = engine;
        }

        private static void Report(Action<string> progress, string message)
        {
            if (progress != null) progress(message);
        }

        public void Start(Action<string> progress)
        {
            Report(progress, "Starting " + Engine.Name + "...");

            Engine.Start();
        }

        public void Stop(Action<string> progress)
        {
            Report(progress, "Stopping " + Engine.Name + "...");

            Engine.Stop();
        }

        public void Restart(Action<string> progress)
        {
            if (Engine.GetState() == EngineState.Running) Stop(progress);

            Start(progress);
        }

        /// <summary>
        /// Runs an action that needs the files of Acrylic not to be in use, stopping and restarting Acrylic around it
        /// if it is running.
        /// </summary>
        public T WithEngineStopped<T>(Func<T> action, Action<string> progress)
        {
            EngineState state = Engine.GetState();

            if (state == EngineState.Busy) throw new InvalidOperationException("Acrylic is in the middle of starting or stopping. Please try again in a moment.");

            bool wasRunning = (state == EngineState.Running);

            if (wasRunning)
            {
                Stop(progress);

                WaitUntilReleased(AcrylicPaths.CacheFile);
            }

            try
            {
                return action();
            }
            finally
            {
                if (wasRunning) Start(progress);
            }
        }

        private static void WaitUntilReleased(string path)
        {
            for (int attempt = 0; attempt < 25; attempt++)
            {
                if (!File.Exists(path)) return;

                try
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }

        public bool IsDebugLogEnabled => File.Exists(AcrylicPaths.DebugLogFile);

        public void SetDebugLogEnabled(bool enabled, Action<string> progress)
        {
            WithEngineStopped(() =>
            {
                if (enabled)
                {
                    Report(progress, "Creating the Acrylic debug log...");

                    if (!File.Exists(AcrylicPaths.DebugLogFile)) File.WriteAllBytes(AcrylicPaths.DebugLogFile, new byte[0]);
                }
                else
                {
                    Report(progress, "Removing the Acrylic debug log...");

                    File.Delete(AcrylicPaths.DebugLogFile);
                }

                return true;
            }, progress);
        }

        public void PurgeCache(Action<string> progress)
        {
            WithEngineStopped(() =>
            {
                Report(progress, "Removing the Acrylic cache file...");

                File.Delete(AcrylicPaths.CacheFile);

                return true;
            }, progress);
        }

        /// <summary>
        /// Brings the cache file up to date with what Acrylic holds in memory. Acrylic writes its address cache to
        /// disk only when it stops, so this is a restart.
        /// </summary>
        public void FlushCache(Action<string> progress)
        {
            WithEngineStopped(() => true, progress);
        }

        /// <summary>
        /// Removes from the address cache every item belonging to the specified domain names and returns what has
        /// been removed.
        ///
        /// The address cache lives in the memory of Acrylic and is saved to AcrylicCache.dat only when Acrylic stops,
        /// so Acrylic is stopped (which brings the file up to date), the items are taken out of the file and Acrylic
        /// is started again, loading the file back.
        /// </summary>
        public List<CacheEntry> ClearCachedDomains(IList<string> domainNames, bool includeSubdomains, Action<string> progress)
        {
            return WithEngineStopped(() =>
            {
                Report(progress, "Removing the items from the address cache...");

                return AddressCacheFile.Remove(AcrylicPaths.CacheFile, entry =>
                {
                    foreach (string domainName in domainNames)
                    {
                        if (AddressCacheFile.DomainMatches(entry.DomainName, domainName, includeSubdomains)) return true;
                    }

                    return false;
                });
            }, progress);
        }

        // ------------------------------------------------------------------------------------------------------------
        // What the configuration file says about the things the UI needs to know
        // ------------------------------------------------------------------------------------------------------------

        public static IniDocument LoadConfiguration()
        {
            return File.Exists(AcrylicPaths.ConfigurationFile) ? IniDocument.Parse(TextFile.Load(AcrylicPaths.ConfigurationFile).Text) : IniDocument.Parse("");
        }

        /// <summary>
        /// Returns the address a local client can reach Acrylic at, according to the configuration file, or null if
        /// Acrylic is not bound to anything.
        /// </summary>
        public static IPEndPoint GetLocalEndPoint(IniDocument configuration)
        {
            string address = configuration.Get(ConfigSchema.GlobalSection, "LocalIPv4BindingAddress") ?? "";

            if (address.Length > 0 && ConfigSchema.IsIPv4(address))
            {
                IPAddress ip = IPAddress.Parse(address);

                if (ip.Equals(IPAddress.Any)) ip = IPAddress.Loopback;

                return new IPEndPoint(ip, ReadPort(configuration, "LocalIPv4BindingPort"));
            }

            address = configuration.Get(ConfigSchema.GlobalSection, "LocalIPv6BindingAddress") ?? "";

            if (address.Length > 0 && ConfigSchema.IsIPv6(address))
            {
                IPAddress ip = IPAddress.Parse(address);

                if (ip.Equals(IPAddress.IPv6Any)) ip = IPAddress.IPv6Loopback;

                return new IPEndPoint(ip, ReadPort(configuration, "LocalIPv6BindingPort"));
            }

            return null;
        }

        private static int ReadPort(IniDocument configuration, string key)
        {
            return int.TryParse(configuration.Get(ConfigSchema.GlobalSection, key), out int port) && port >= 1 && port <= 65535 ? port : 53;
        }

        public static int ReadMinutes(IniDocument configuration, string key, int builtIn)
        {
            return int.TryParse(configuration.Get(ConfigSchema.GlobalSection, key), out int value) ? value : builtIn;
        }
    }
}
