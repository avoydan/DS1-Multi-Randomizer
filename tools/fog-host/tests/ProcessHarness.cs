using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FogHost.Tests
{
    internal static class ProcessHarness
    {
        internal const int MaxConcurrentChildProcesses = 4;
        internal static readonly TimeSpan HostTimeout = TimeSpan.FromMinutes(3);

        private static readonly SemaphoreSlim ChildProcessSlots = new SemaphoreSlim(MaxConcurrentChildProcesses, MaxConcurrentChildProcesses);
        private static readonly ConcurrentBag<int> SpawnedHostPids = new ConcurrentBag<int>();
        private static HashSet<int> baselineHostPids = new HashSet<int>();
        private static string configuredHostPath;
        private static int totalSpawned;
        private static int activeHosts;
        private static int peakHosts;

        internal sealed record Result(int ExitCode, string Stdout, string Stderr, bool TimedOut, int ProcessId, string CleanupError);

        internal sealed record Metrics(int Spawned, int PeakConcurrent, int Limit);

        internal static string Configure(string configuredPath, string defaultPath)
        {
            string candidate = string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;
            if (string.IsNullOrWhiteSpace(candidate))
                throw new InvalidOperationException("FOGHOST_EXE and the default FogHost.exe path are both empty.");

            string fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"FogHost test executable was not found: {fullPath}", fullPath);

            string entryAssembly = Assembly.GetEntryAssembly()?.Location;
            string appHost = Path.Combine(AppContext.BaseDirectory, "FogHost.Tests.exe");
            string[] selfPaths = new[] { Environment.ProcessPath, entryAssembly, appHost }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .ToArray();
            bool isSelf = selfPaths.Any(path => PathsEqual(path, fullPath))
                || string.Equals(Path.GetFileNameWithoutExtension(fullPath), "FogHost.Tests", StringComparison.OrdinalIgnoreCase);
            if (isSelf)
                throw new InvalidOperationException($"FOGHOST_EXE resolves to the FogHost test runner itself ({fullPath}); refusing recursive test-suite launch.");

            configuredHostPath = fullPath;
            return fullPath;
        }

        internal static void CaptureBaseline(string hostPath)
        {
            configuredHostPath = Path.GetFullPath(hostPath);
            baselineHostPids = MatchingHostProcesses(configuredHostPath, out _).Select(process =>
            {
                int id = process.Id;
                process.Dispose();
                return id;
            }).ToHashSet();
        }

        internal static Result RunHost(string executable, IEnumerable<string> arguments, TimeSpan? timeout = null)
        {
            string fullPath = Path.GetFullPath(executable);
            if (!PathsEqual(fullPath, configuredHostPath))
                throw new InvalidOperationException($"Test attempted to launch an unconfigured FogHost executable: {fullPath}");

            var start = CreateStartInfo(fullPath, arguments);
            return Execute(start, timeout ?? HostTimeout, countAsFogHost: true);
        }

        private static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            return start;
        }

        private static Result Execute(ProcessStartInfo start, TimeSpan timeout, bool countAsFogHost)
        {
            ChildProcessSlots.Wait();
            int processId = 0;
            bool countedActive = false;
            try
            {
                using var process = new Process { StartInfo = start };
                if (!process.Start()) throw new InvalidOperationException($"Could not start child process: {start.FileName}");
                processId = process.Id;

                if (countAsFogHost)
                {
                    SpawnedHostPids.Add(processId);
                    Interlocked.Increment(ref totalSpawned);
                    int active = Interlocked.Increment(ref activeHosts);
                    countedActive = true;
                    UpdatePeak(active);
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                bool timedOut = false;
                string cleanupError = null;
                try
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    cleanupError = TerminateTreeAndReap(process);
                }
                catch
                {
                    if (!process.HasExited) _ = TerminateTreeAndReap(process);
                    throw;
                }

                bool streamsClosed = Task.WaitAll(new Task[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(10));
                if (!streamsClosed)
                    cleanupError = JoinErrors(cleanupError, "Redirected output streams did not close after process exit.");
                string stdout = streamsClosed ? stdoutTask.GetAwaiter().GetResult() : string.Empty;
                string stderr = streamsClosed ? stderrTask.GetAwaiter().GetResult() : string.Empty;
                return new Result(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut, processId, cleanupError);
            }
            finally
            {
                if (countedActive) Interlocked.Decrement(ref activeHosts);
                ChildProcessSlots.Release();
            }
        }

        private static string TerminateTreeAndReap(Process process)
        {
            string killError = null;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception error)
            {
                if (!process.HasExited)
                {
                    killError = $"Kill(entireProcessTree: true) failed: {error.Message}";
                    try { process.Kill(); }
                    catch (Exception fallback) { killError = JoinErrors(killError, $"Direct child kill also failed: {fallback.Message}"); }
                }
            }

            try
            {
                if (!process.WaitForExit(milliseconds: 15000))
                    return JoinErrors(killError, "Direct child process did not exit within 15 seconds after timeout cleanup.");
            }
            catch (Exception error)
            {
                return JoinErrors(killError, $"Waiting for timed-out process failed: {error.Message}");
            }
            return killError;
        }

        private static void UpdatePeak(int active)
        {
            while (true)
            {
                int current = Volatile.Read(ref peakHosts);
                if (active <= current || Interlocked.CompareExchange(ref peakHosts, active, current) == current) return;
            }
        }

        internal static Metrics Snapshot() => new Metrics(
            Volatile.Read(ref totalSpawned),
            Volatile.Read(ref peakHosts),
            MaxConcurrentChildProcesses);

        internal static void VerifySelfLaunchRejected()
        {
            string self = Assembly.GetEntryAssembly()?.Location
                ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot identify the FogHost test runner executable.");
            try
            {
                Configure(self, self);
            }
            catch (InvalidOperationException error) when (error.Message.Contains("refusing recursive test-suite launch", StringComparison.Ordinal))
            {
                return;
            }
            throw new InvalidOperationException("The FogHost test runner was accepted as its own child executable.");
        }

        internal static void VerifyConcurrencyLimit(string hostPath, string scratchRoot)
        {
            const int launchCount = 8;
            int before = Snapshot().Spawned;
            Task<Result>[] launches = Enumerable.Range(0, launchCount)
                .Select(index => Task.Run(() => RunHost(hostPath,
                    new[] { "foghost-test-invalid-argument" })))
                .ToArray();
            Task.WaitAll(launches);

            if (Snapshot().Spawned - before != launchCount)
                throw new InvalidOperationException($"Expected {launchCount} spawned FogHost children, observed {Snapshot().Spawned - before}.");
            for (int index = 0; index < launches.Length; index++)
            {
                Result result = launches[index].Result;
                if (result.TimedOut || result.CleanupError != null || result.ExitCode != 2)
                    throw new InvalidOperationException($"Invalid-argument child {index} did not exit cleanly with code 2: exit={result.ExitCode}, timeout={result.TimedOut}, cleanup={result.CleanupError}, stderr={result.Stderr}");
            }

            Metrics metrics = Snapshot();
            if (metrics.PeakConcurrent > MaxConcurrentChildProcesses)
                throw new InvalidOperationException($"Peak FogHost concurrency {metrics.PeakConcurrent} exceeded the hard limit {MaxConcurrentChildProcesses}.");
        }

        internal static void VerifyTimeoutKillsProcessTree(string scratchRoot)
        {
            if (!OperatingSystem.IsWindows())
                throw new InvalidOperationException("The FogHost process-tree lifecycle regression is Windows-only.");

            Directory.CreateDirectory(scratchRoot);
            string markerPath = Path.Combine(scratchRoot, "timeout-child-" + Guid.NewGuid().ToString("N") + ".pid");
            ProcessStartInfo start = CreateTestRunnerStartInfo("--foghost-test-process-tree-child", markerPath);
            Result result = Execute(start, TimeSpan.FromSeconds(3), countAsFogHost: false);
            if (!result.TimedOut)
                throw new InvalidOperationException($"Expected test runner timeout; got exit={result.ExitCode}, stderr={result.Stderr}");
            if (result.CleanupError != null)
                throw new InvalidOperationException($"Timed-out process cleanup reported an error: {result.CleanupError}");
            if (!File.Exists(markerPath))
                throw new InvalidOperationException("The process-tree helper did not start its grandchild before timeout.");

            int grandchildPid = int.Parse(File.ReadAllText(markerPath).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            if (!WaitForProcessExit(grandchildPid, TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException($"Timed-out process tree left grandchild PID {grandchildPid} running.");
            if (!WaitForProcessExit(result.ProcessId, TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException($"Timed-out test runner PID {result.ProcessId} was not reaped.");
        }

        internal static int RunProcessTreeChild(string markerPath)
        {
            if (!OperatingSystem.IsWindows()) return 2;
            var childStart = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            childStart.ArgumentList.Add("-NoProfile");
            childStart.ArgumentList.Add("-Command");
            childStart.ArgumentList.Add("Start-Sleep -Seconds 60");
            using Process child = Process.Start(childStart)
                ?? throw new InvalidOperationException("Could not start the process-tree grandchild.");
            File.WriteAllText(markerPath, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            child.WaitForExit();
            return child.ExitCode;
        }

        internal static void VerifyNoOrphans(string hostPath)
        {
            string fullPath = Path.GetFullPath(hostPath);
            int ownedRemaining = SpawnedHostPids.Distinct().Count(pid => IsSameLiveProcess(pid, fullPath));
            int newMatchingProcesses = MatchingHostProcesses(fullPath, out _)
                .Count(process =>
                {
                    bool isNew = !baselineHostPids.Contains(process.Id);
                    process.Dispose();
                    return isNew;
                });
            if (ownedRemaining != 0 || newMatchingProcesses != 0)
                throw new InvalidOperationException($"FogHost children remain after suite completion: ownedPids={ownedRemaining}, newSamePathProcesses={newMatchingProcesses}.");
        }

        internal static void PrintMetrics(string hostPath)
        {
            Metrics metrics = Snapshot();
            string fullPath = Path.GetFullPath(hostPath);
            int ownedRemaining = SpawnedHostPids.Distinct().Count(pid => IsSameLiveProcess(pid, fullPath));
            List<Process> matching = MatchingHostProcesses(fullPath, out int inaccessible);
            int newMatchingProcesses = matching.Count(process => !baselineHostPids.Contains(process.Id));
            foreach (Process process in matching) process.Dispose();
            Console.WriteLine($"FogHost process telemetry: spawned={metrics.Spawned}, peakConcurrent={metrics.PeakConcurrent}/{metrics.Limit}, ownedRemaining={ownedRemaining}, newSamePathProcessesAfterBaseline={newMatchingProcesses}, processPathReadFailures={inaccessible}.");
        }

        private static ProcessStartInfo CreateTestRunnerStartInfo(params string[] arguments)
        {
            string processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot locate the current test runner process.");
            var start = new ProcessStartInfo(processPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string assembly = Assembly.GetEntryAssembly()?.Location
                    ?? throw new InvalidOperationException("Cannot locate the test runner assembly for dotnet execution.");
                start.ArgumentList.Add(assembly);
            }
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            return start;
        }

        private static bool WaitForProcessExit(int processId, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    using Process process = Process.GetProcessById(processId);
                    if (process.HasExited) return true;
                }
                catch (ArgumentException)
                {
                    return true;
                }
                Thread.Sleep(50);
            }
            return false;
        }

        private static bool IsSameLiveProcess(int processId, string expectedPath)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited) return false;
                return PathsEqual(process.MainModule?.FileName, expectedPath);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return true;
            }
        }

        private static List<Process> MatchingHostProcesses(string expectedPath, out int inaccessible)
        {
            inaccessible = 0;
            string processName = Path.GetFileNameWithoutExtension(expectedPath);
            var matching = new List<Process>();
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (PathsEqual(process.MainModule?.FileName, expectedPath)) matching.Add(process);
                    else process.Dispose();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    inaccessible++;
                    process.Dispose();
                }
                catch (InvalidOperationException)
                {
                    process.Dispose();
                }
            }
            return matching;
        }

        private static string JoinErrors(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first)) return second;
            if (string.IsNullOrWhiteSpace(second)) return first;
            return first + " " + second;
        }

        private static bool PathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
    }
}