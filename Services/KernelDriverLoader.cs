using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace ExteraMonitor.Services;

/// <summary>Loads ExteraMonitorDriver through the bundled KVC utility and records each startup stage.</summary>
public sealed class KernelDriverLoader : IDisposable
{
    private const string DriverFileName = "ExteraMonitorDriver.sys";
    private const string ServiceName = "ExteraMonitorDriver";
    private static readonly TimeSpan DriverStartDeadline = TimeSpan.FromSeconds(10);
    private static int _fatalFailureSignalled;
    private static readonly TimeSpan LoadRetryInterval = TimeSpan.FromSeconds(10);
    private readonly string _driverDirectory = Path.Combine(AppContext.BaseDirectory, "Driver");
    private bool _loaded;
    private DateTimeOffset _nextAutomaticRetryUtc;
    private bool _hasReportedConnected;

    public static event Action? DriverLoadFailed;

    public bool TryLoad() => TryLoad(automatic: false);

    public bool IsLoaded => OperatingSystem.IsWindows() && QueryService().IsRunning;

    public bool EnsureLoaded()
    {
        if (Volatile.Read(ref _fatalFailureSignalled) != 0) return false;
        if (!OperatingSystem.IsWindows())
        {
            DriverDiagnostics.Write("platform", $"Driver loading is Windows-only; OS={Environment.OSVersion}.");
            return false;
        }

        var service = QueryService();
        if (service.IsRunning)
        {
            _loaded = true;
            if (!_hasReportedConnected)
            {
                _hasReportedConnected = true;
                DriverDiagnostics.Write("service.ready", $"Service={ServiceName}; state=RUNNING; source=already-loaded service.",
                    "Driver service is already running; checking device access and sensors next.", "driver-ready");
            }
            return true;
        }

        if (DateTimeOffset.UtcNow < _nextAutomaticRetryUtc) return false;
        _nextAutomaticRetryUtc = DateTimeOffset.UtcNow + LoadRetryInterval;
        return TryLoad(automatic: true, previousService: service);
    }

    public bool TryReload(bool automatic = false)
    {
        if (Volatile.Read(ref _fatalFailureSignalled) != 0) return false;
        if (!OperatingSystem.IsWindows())
        {
            DriverDiagnostics.Write("reload.platform", $"Reload unavailable; OS={Environment.OSVersion}.");
            return false;
        }

        if (automatic && DateTimeOffset.UtcNow < _nextAutomaticRetryUtc) return false;
        _nextAutomaticRetryUtc = DateTimeOffset.UtcNow + LoadRetryInterval;

        LogEnvironment("reload.begin");
        var files = CheckRuntimeFiles();
        if (!files.Ready) return false;

        var deadline = DateTimeOffset.UtcNow + DriverStartDeadline;
        DriverDiagnostics.Write("kvc.reload.begin", $"Utility={files.KvcPath}; driver={files.DriverPath}; startupDeadlineSeconds=10.",
            "Starting KVC driver reload. The driver must be RUNNING within 10 seconds.", "kvc-reload-launching");
        var command = RunKvc(files.KvcPath, "reload", files.DriverPath, RemainingTime(deadline));
        LogCommand("kvc.reload", command, notifyTelegram: true);
        var service = WaitForRunningService(deadline);
        _loaded = service.IsRunning;
        _nextAutomaticRetryUtc = DateTimeOffset.UtcNow + LoadRetryInterval;
        ReportFinalState("reload", command, service);
        return _loaded;
    }

    public bool TryStopAndRemove()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var before = QueryService();
        DriverDiagnostics.Write("service.remove.begin", $"State={before.State}; scExit={before.ExitCode}; query={Flatten(before.Output)}.");
        if (before.Exists)
        {
            var stop = RunSc("stop", ServiceName);
            LogCommand("sc.stop", stop);
            var delete = RunSc("delete", ServiceName);
            LogCommand("sc.delete", delete);
        }

        var removed = WaitForServiceRemoval();
        _loaded = false;
        DriverDiagnostics.Write(removed ? "service.remove.success" : "service.remove.timeout",
            $"Service={ServiceName}; removed={removed}; finalQuery={FormatService(QueryService())}.",
            removed ? "Driver service stopped and removed." : "Driver service could not be removed; see the local driver log.",
            removed ? "driver-remove-success" : "driver-remove-failure");
        return removed;
    }

    public void Dispose()
    {
        // The monitor closes its device handle but intentionally keeps the service resident.
        _loaded = false;
    }

    private bool TryLoad(bool automatic, ServiceQuery? previousService = null)
    {
        if (Volatile.Read(ref _fatalFailureSignalled) != 0) return false;
        if (!OperatingSystem.IsWindows()) return false;

        LogEnvironment(automatic ? "load.automatic.begin" : "load.manual.begin");
        var service = previousService ?? QueryService();
        if (service.IsRunning)
        {
            _loaded = true;
            return true;
        }

        var serviceDetail = $"Service={ServiceName}; {FormatService(service)}.";
        DriverDiagnostics.Write("service.preload", serviceDetail, serviceDetail, "service-preload");
        var files = CheckRuntimeFiles();
        if (!files.Ready) return false;

        var kvcMessage = $"Action=driver load; utility={files.KvcPath}; driver={files.DriverPath}; automatic={automatic}; startupDeadlineSeconds=10.";
        var deadline = DateTimeOffset.UtcNow + DriverStartDeadline;
        DriverDiagnostics.Write("kvc.load.begin", kvcMessage, $"Starting KVC driver load now. Automatic retry={automatic}; driver service={ServiceName}; deadline=10 seconds.", "kvc-load-begin");
        var command = RunKvc(files.KvcPath, "load", files.DriverPath, RemainingTime(deadline));
        LogCommand("kvc.load", command, notifyTelegram: true);
        var after = WaitForRunningService(deadline);
        _loaded = after.IsRunning;
        _nextAutomaticRetryUtc = DateTimeOffset.UtcNow + LoadRetryInterval;
        ReportFinalState("load", command, after);
        return _loaded;
    }

    private (bool Ready, string KvcPath, string DriverPath) CheckRuntimeFiles()
    {
        var kvcPath = Path.Combine(_driverDirectory, "kvc.exe");
        var driverPath = Path.Combine(_driverDirectory, DriverFileName);
        var dataPath = Path.Combine(_driverDirectory, "kvc.dat");
        var kvcExists = File.Exists(kvcPath);
        var dataExists = File.Exists(dataPath);
        var driverExists = File.Exists(driverPath);
        var detail = $"directory={_driverDirectory}; kvc.exe={DescribeFile(kvcPath)}; kvc.dat={DescribeFile(dataPath)}; {DriverFileName}={DescribeFile(driverPath)}";
        var filesReady = kvcExists && dataExists && driverExists;
        DriverDiagnostics.Write("package.check", detail,
            filesReady ? $"Driver package check: {DriverDiagnostics.Limit(detail, 2_800)}" : "Driver startup failed: one or more required KVC/driver files are missing. See the local driver log.",
            filesReady ? "driver-package-check" : "driver-files-missing");
        if (!filesReady) DriverDiagnostics.QueueLogUpload();
        return (kvcExists && dataExists && driverExists, kvcPath, driverPath);
    }

    private static string DescribeFile(string path)
    {
        if (!File.Exists(path)) return "missing";
        try
        {
            var info = new FileInfo(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            return $"present; bytes={info.Length}; sha256={hash}";
        }
        catch (Exception ex)
        {
            return $"present; metadataError={ex.GetType().Name}:{ex.Message}";
        }
    }

    private static void LogEnvironment(string stage)
    {
        var elevated = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                DriverDiagnostics.Write("environment.identity", $"Could not determine elevation: {ex.GetType().Name}: {ex.Message}");
            }
        }

        DriverDiagnostics.Write(stage,
            $"OS={Environment.OSVersion}; 64BitOS={Environment.Is64BitOperatingSystem}; 64BitProcess={Environment.Is64BitProcess}; elevated={elevated}; appBase={AppContext.BaseDirectory}; log={DriverDiagnostics.LogFilePath}",
            $"{stage}: OS={Environment.OSVersion}; 64-bit OS={Environment.Is64BitOperatingSystem}; 64-bit process={Environment.Is64BitProcess}; elevated={elevated}.", stage);
    }

    private static CommandResult RunKvc(string kvc, string action, string driver, TimeSpan timeout)
    {
        var info = new ProcessStartInfo
        {
            FileName = kvc,
            WorkingDirectory = Path.GetDirectoryName(kvc) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("driver");
        info.ArgumentList.Add(action);
        info.ArgumentList.Add(driver);
        return RunProcess(info, timeout);
    }

    private static CommandResult RunSc(params string[] arguments) =>
        RunSc(TimeSpan.FromSeconds(10), arguments);

    private static CommandResult RunSc(TimeSpan timeout, params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return RunProcess(info, timeout);
    }

    private static CommandResult RunProcess(ProcessStartInfo info, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = new Process { StartInfo = info };
            if (!process.Start()) return new CommandResult(false, null, false, "", "Process.Start returned false.", stopwatch.Elapsed);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = !process.WaitForExit((int)timeout.TotalMilliseconds);
            if (timedOut)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                process.WaitForExit();
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            stopwatch.Stop();
            return new CommandResult(true, process.ExitCode, timedOut, stdout, stderr, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CommandResult(false, null, false, "", $"{ex.GetType().Name}: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private static ServiceQuery QueryService(TimeSpan? timeout = null)
    {
        var command = RunSc(timeout ?? TimeSpan.FromSeconds(10), "query", ServiceName);
        var stateMatch = Regex.Match(command.Output, @"STATE\s*:\s*\d+\s+([A-Z_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var state = stateMatch.Success ? stateMatch.Groups[1].Value.ToUpperInvariant() : "UNKNOWN";
        var exists = command.ExitCode == 0 || stateMatch.Success;
        return new ServiceQuery(exists, state == "RUNNING", state, command.ExitCode, command.Output, command.Error, command.TimedOut);
    }

    private static ServiceQuery WaitForRunningService(DateTimeOffset deadline)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        var last = QueryService(remaining > TimeSpan.FromMilliseconds(100) ? Min(remaining, TimeSpan.FromSeconds(1)) : TimeSpan.FromMilliseconds(100));
        while (!last.IsRunning && DateTimeOffset.UtcNow < deadline)
        {
            remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            Thread.Sleep((int)Math.Min(200, remaining.TotalMilliseconds));
            remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) last = QueryService(Min(remaining, TimeSpan.FromSeconds(1)));
        }
        return last;
    }

    private static TimeSpan RemainingTime(DateTimeOffset deadline)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static bool WaitForServiceRemoval()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var service = QueryService();
            if (!service.Exists && service.ExitCode == 1060) return true;
            Thread.Sleep(250);
        }
        return false;
    }

    private static void LogCommand(string stage, CommandResult command, bool notifyTelegram = false)
    {
        var detail = $"started={command.Started}; exitCode={command.ExitCode?.ToString() ?? "n/a"}; timeout={command.TimedOut}; durationMs={command.Duration.TotalMilliseconds:0}; stdout={Flatten(command.Output)}; stderr={Flatten(command.Error)}";
        DriverDiagnostics.Write(stage, detail,
            notifyTelegram ? $"{stage}: {DriverDiagnostics.Limit(detail, 2_800)}" : null,
            notifyTelegram ? stage : null);
    }

    private void ReportFinalState(string action, CommandResult command, ServiceQuery service)
    {
        var succeeded = service.IsRunning;
        _hasReportedConnected = succeeded;
        var summary = succeeded
            ? $"Driver {action} succeeded; service is RUNNING; KVC exit={command.ExitCode?.ToString() ?? "n/a"}."
            : $"Driver {action} failed; service state={service.State}; KVC exit={command.ExitCode?.ToString() ?? "n/a"}; see local driver log.";
        DriverDiagnostics.Write(succeeded ? "service.ready" : "service.failed",
            $"action={action}; kvcExit={command.ExitCode?.ToString() ?? "n/a"}; kvcTimeout={command.TimedOut}; service={FormatService(service)}; log={DriverDiagnostics.LogFilePath}",
            $"{summary} Service details: {DriverDiagnostics.Limit(FormatService(service), 2_000)}", succeeded ? "driver-ready" : "driver-failure");
        if (!succeeded)
        {
            if (Interlocked.Exchange(ref _fatalFailureSignalled, 1) == 0)
            {
                DriverDiagnostics.Write("driver.load.terminal-failure", $"Driver {action} did not reach RUNNING before its 10-second deadline. Signalling application shutdown.",
                    $"driver load failed: service did not reach RUNNING within 10 seconds. KVC exit={command.ExitCode?.ToString() ?? "n/a"}; service={service.State}. Sending KVC output and closing Extera Monitor.",
                    "driver-load-terminal-failure");
                var kvcArtifact = $"Driver action: {action}{Environment.NewLine}" +
                    $"Started: {command.Started}{Environment.NewLine}Exit code: {command.ExitCode?.ToString() ?? "n/a"}{Environment.NewLine}" +
                    $"Timed out: {command.TimedOut}{Environment.NewLine}Duration: {command.Duration.TotalMilliseconds:0} ms{Environment.NewLine}" +
                    "Startup deadline: 10 seconds" + Environment.NewLine +
                    $"Service after KVC: {FormatService(service)}{Environment.NewLine}{Environment.NewLine}" +
                    "----- KVC stdout -----" + Environment.NewLine + command.Output + Environment.NewLine +
                    "----- KVC stderr -----" + Environment.NewLine + command.Error + Environment.NewLine;
                DriverDiagnostics.SaveAndQueueArtifact($"kvc-{action}-failure.txt", $"KVC output: driver {action} failed; service state {service.State}.", kvcArtifact);
                DriverDiagnostics.QueueLogUpload();
                DriverLoadFailed?.Invoke();
            }
        }
    }

    private static string FormatService(ServiceQuery service) =>
        $"exists={service.Exists}; state={service.State}; exitCode={service.ExitCode?.ToString() ?? "n/a"}; timeout={service.TimedOut}; stdout={Flatten(service.Output)}; stderr={Flatten(service.Error)}";

    private static string Flatten(string value) => DriverDiagnostics.Limit(value.Trim().Replace('\r', ' ').Replace('\n', ' '), 2_000);

    private sealed record CommandResult(bool Started, int? ExitCode, bool TimedOut, string Output, string Error, TimeSpan Duration);
    private sealed record ServiceQuery(bool Exists, bool IsRunning, string State, int? ExitCode, string Output, string Error, bool TimedOut);
}
