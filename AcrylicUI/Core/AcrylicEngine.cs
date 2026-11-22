using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace AcrylicUI.Core
{
    public enum EngineState
    {
        /// <summary>The Acrylic service is not installed (or, in sandbox mode, AcrylicConsole.exe is missing).</summary>
        NotInstalled,
        Stopped,
        Running,

        /// <summary>The service is in the middle of starting or stopping.</summary>
        Busy,

        /// <summary>An Acrylic service is installed, but from a directory other than the one of this UI.</summary>
        Foreign
    }

    /// <summary>
    /// The running part of Acrylic the UI controls: the Windows service, or a private console process in sandbox mode.
    /// </summary>
    public abstract class AcrylicEngine : IDisposable
    {
        public abstract bool IsSandbox { get; }

        /// <summary>How the engine is called in messages, for example "the Acrylic service".</summary>
        public abstract string Name { get; }

        public abstract EngineState GetState();

        public abstract void Start();
        public abstract void Stop();

        public abstract void Install();
        public abstract void Uninstall();

        /// <summary>When the state is Foreign: the executable of the service that is installed.</summary>
        public virtual string ForeignExecutable => null;

        public virtual void Dispose()
        {
        }

        public static AcrylicEngine Create()
        {
            if (AcrylicPaths.IsSandbox) return new SandboxEngine();

            return new ServiceEngine();
        }

        protected static int Run(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AcrylicPaths.Directory };

            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();

                return process.ExitCode;
            }
        }
    }

    /// <summary>
    /// The Acrylic Windows service.
    /// </summary>
    public sealed class ServiceEngine : AcrylicEngine
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

        private string foreignExecutable;

        public override bool IsSandbox => false;

        public override string Name => "the Acrylic service";

        public override string ForeignExecutable => foreignExecutable;

        public override EngineState GetState()
        {
            string imagePath;

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + AcrylicPaths.ServiceName))
            {
                if (key == null) return EngineState.NotInstalled;

                imagePath = key.GetValue("ImagePath") as string;
            }

            string installedExecutable = ExtractExecutable(imagePath);

            if (installedExecutable != null && !SamePath(installedExecutable, AcrylicPaths.ServiceExecutable))
            {
                // The service belongs to another copy of Acrylic: starting or stopping it from here would act on files
                // other than the ones this UI shows, so it is reported as something this UI must not touch.

                foreignExecutable = installedExecutable;

                return EngineState.Foreign;
            }

            foreignExecutable = null;

            try
            {
                using (var controller = new ServiceController(AcrylicPaths.ServiceName))
                {
                    switch (controller.Status)
                    {
                        case ServiceControllerStatus.Running: return EngineState.Running;
                        case ServiceControllerStatus.Stopped: return EngineState.Stopped;
                        default: return EngineState.Busy;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                return EngineState.NotInstalled;
            }
        }

        private static string ExtractExecutable(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return null;

            imagePath = Environment.ExpandEnvironmentVariables(imagePath.Trim());

            if (imagePath[0] == '"')
            {
                int closing = imagePath.IndexOf('"', 1);

                return closing > 0 ? imagePath.Substring(1, closing - 1) : imagePath.Substring(1);
            }

            int extension = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);

            return extension > 0 ? imagePath.Substring(0, extension + 4) : imagePath;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void EnsureOwned()
        {
            if (GetState() == EngineState.Foreign) throw new InvalidOperationException("The Acrylic service installed on this computer runs from \"" + foreignExecutable + "\", not from this directory, so it cannot be managed from here.");
        }

        public override void Start()
        {
            EnsureOwned();

            using (var controller = new ServiceController(AcrylicPaths.ServiceName))
            {
                if (controller.Status == ServiceControllerStatus.Running) return;

                if (controller.Status != ServiceControllerStatus.StartPending) controller.Start();

                controller.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);
            }
        }

        public override void Stop()
        {
            EnsureOwned();

            using (var controller = new ServiceController(AcrylicPaths.ServiceName))
            {
                if (controller.Status == ServiceControllerStatus.Stopped) return;

                if (controller.Status != ServiceControllerStatus.StopPending) controller.Stop();

                controller.WaitForStatus(ServiceControllerStatus.Stopped, StopTimeout);
            }
        }

        public override void Install()
        {
            EngineState state = GetState();

            if (state == EngineState.Foreign) EnsureOwned();

            if (state != EngineState.NotInstalled) return;

            if (!File.Exists(AcrylicPaths.ServiceExecutable)) throw new FileNotFoundException("AcrylicService.exe was not found in \"" + AcrylicPaths.Directory + "\".");

            if (Run(AcrylicPaths.ServiceExecutable, "/INSTALL /SILENT") != 0) throw new InvalidOperationException("AcrylicService.exe reported an error while installing the service.");

            // Same hardening the previous versions of the UI applied to the executable of the service.

            Run("ICACLS.exe", "\"" + AcrylicPaths.ServiceExecutable + "\" /inheritance:d");
            Run("ICACLS.exe", "\"" + AcrylicPaths.ServiceExecutable + "\" /remove:g \"Authenticated Users\"");
        }

        public override void Uninstall()
        {
            EngineState state = GetState();

            if (state == EngineState.Foreign) EnsureOwned();

            if (state == EngineState.NotInstalled) return;

            if (state != EngineState.Stopped) Stop();

            if (Run(AcrylicPaths.ServiceExecutable, "/UNINSTALL /SILENT") != 0) throw new InvalidOperationException("AcrylicService.exe reported an error while uninstalling the service.");
        }
    }

    /// <summary>
    /// A private AcrylicConsole.exe process owned by the UI. It reads the configuration, the hosts file and the cache
    /// found next to the UI, exactly like the service would, but it is not registered anywhere in the system and it
    /// dies with the UI. It is what makes it possible to try Acrylic out without touching an installed service.
    /// </summary>
    public sealed class SandboxEngine : AcrylicEngine
    {
        private readonly object sync = new object();

        private Process process;
        private Thread logPump;
        private IntPtr job = IntPtr.Zero;

        public override bool IsSandbox => true;

        public override string Name => "the sandboxed Acrylic engine";

        public override EngineState GetState()
        {
            lock (sync)
            {
                if (process != null && !process.HasExited) return EngineState.Running;
            }

            return File.Exists(AcrylicPaths.ConsoleExecutable) ? EngineState.Stopped : EngineState.NotInstalled;
        }

        /// <summary>
        /// The sandbox must never compete with an installed Acrylic service for the standard DNS port.
        /// </summary>
        public static string FindBindingConflict(IniDocument configuration)
        {
            foreach (string family in new[] { "IPv4", "IPv6" })
            {
                string address = configuration.Get(ConfigSchema.GlobalSection, "Local" + family + "BindingAddress") ?? "";

                if (address.Length == 0) continue;

                string port = configuration.Get(ConfigSchema.GlobalSection, "Local" + family + "BindingPort") ?? "";

                if (port.Length == 0 || port == "53") return "The configuration binds Acrylic to the standard DNS port 53 on " + family + ". The sandbox refuses to do that, because port 53 belongs to the Acrylic service installed on this computer. Choose another port (for example 15353) in Configuration > Local binding and save.";
            }

            return null;
        }

        public override void Start()
        {
            lock (sync)
            {
                if (process != null && !process.HasExited) return;

                if (!File.Exists(AcrylicPaths.ConsoleExecutable)) throw new FileNotFoundException("AcrylicConsole.exe was not found in \"" + AcrylicPaths.Directory + "\".");

                string conflict = FindBindingConflict(IniDocument.Parse(TextFile.Load(AcrylicPaths.ConfigurationFile).Text));

                if (conflict != null) throw new InvalidOperationException(conflict);

                bool debugLog = File.Exists(AcrylicPaths.DebugLogFile);

                var startInfo = new ProcessStartInfo(AcrylicPaths.ConsoleExecutable)
                {
                    Arguments = debugLog ? "/NoBanner" : "/NoBanner /NoLog",
                    WorkingDirectory = AcrylicPaths.Directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = debugLog
                };

                Process started = Process.Start(startInfo);

                AssignToKillOnCloseJob(started);

                if (debugLog)
                {
                    // The console version writes its log to the standard output: it is appended to the debug log file,
                    // which is where the service version writes it.

                    logPump = new Thread(() => PumpLog(started)) { IsBackground = true, Name = "Acrylic log pump" };

                    logPump.Start();
                }

                process = started;
            }

            // Give the engine the time to bind its sockets, and notice if it gave up immediately.

            Thread.Sleep(400);

            lock (sync)
            {
                if (process.HasExited) throw new InvalidOperationException("AcrylicConsole.exe stopped right after starting (exit code " + process.ExitCode + ").");
            }
        }

        private static void PumpLog(Process source)
        {
            try
            {
                using (var log = new FileStream(AcrylicPaths.DebugLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    var buffer = new byte[4096];

                    Stream output = source.StandardOutput.BaseStream;

                    int read;

                    while ((read = output.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        log.Write(buffer, 0, read);
                        log.Flush();
                    }
                }
            }
            catch (Exception)
            {
                // Losing the log must never take the engine down: keep draining so the engine does not block.

                try { while (source.StandardOutput.BaseStream.ReadByte() >= 0) { } } catch (Exception) { }
            }
        }

        public override void Stop()
        {
            Process running;

            lock (sync)
            {
                running = process;

                if (running == null || running.HasExited) { process = null; return; }
            }

            try
            {
                // The console version of Acrylic stops gracefully, saving its address cache, when ENTER is pressed.

                running.StandardInput.WriteLine();
                running.StandardInput.Flush();
            }
            catch (IOException)
            {
            }

            if (!running.WaitForExit(30000))
            {
                try { running.Kill(); } catch (Exception) { }

                running.WaitForExit(5000);
            }

            Thread pump = logPump;

            if (pump != null) pump.Join(2000);

            lock (sync)
            {
                process = null; logPump = null;
            }

            running.Dispose();
        }

        public override void Install()
        {
            throw new InvalidOperationException("The sandbox does not install anything on this computer.");
        }

        public override void Uninstall()
        {
            throw new InvalidOperationException("The sandbox does not install anything on this computer.");
        }

        public override void Dispose()
        {
            try { Stop(); } catch (Exception) { }

            if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
        }

        // ------------------------------------------------------------------------------------------------------------
        // A job object guarantees that the engine never outlives the UI, even if the UI is killed.
        // ------------------------------------------------------------------------------------------------------------

        private void AssignToKillOnCloseJob(Process target)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);

                    if (job == IntPtr.Zero) return;

                    var information = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();

                    information.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));

                    IntPtr pointer = Marshal.AllocHGlobal(length);

                    try
                    {
                        Marshal.StructureToPtr(information, pointer, false);

                        SetInformationJobObject(job, JobObjectExtendedLimitInformation, pointer, (uint)length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pointer);
                    }
                }

                AssignProcessToJobObject(job, target.Handle);
            }
            catch (Exception)
            {
            }
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
