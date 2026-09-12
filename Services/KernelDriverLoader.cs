using System.Diagnostics;

namespace ExteraMonitor.Services;

/// <summary>Loads the ExteraMonitorDriver kernel driver only through the bundled kvc utility.</summary>
public sealed class KernelDriverLoader : IDisposable
{
    private const string DriverFileName = "ExteraMonitorDriver.sys";
    private const string ServiceName = "ExteraMonitorDriver";
    private readonly string _driverDirectory;
    private bool _loaded;

    public KernelDriverLoader()
    {
        _driverDirectory = Path.Combine(AppContext.BaseDirectory, "Driver");
    }

    public bool TryLoad()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (QueryService(ServiceName))
        {
            _loaded = true;
            return true;
        }
        var kvc = Path.Combine(_driverDirectory, "kvc.exe");
        var driver = Path.Combine(_driverDirectory, DriverFileName);
        if (!File.Exists(kvc) || !File.Exists(driver)) return false;
        RunKvc(kvc, "load", driver);
        _loaded = WaitForService(ServiceName);
        return _loaded;
    }

    public bool IsLoaded => OperatingSystem.IsWindows() && QueryService(ServiceName);

    public bool EnsureLoaded()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (QueryService(ServiceName))
        {
            _loaded = true;
            return true;
        }
        return TryLoad();
    }

    public bool TryReload()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var kvc = Path.Combine(_driverDirectory, "kvc.exe");
        var driver = Path.Combine(_driverDirectory, DriverFileName);
        if (!File.Exists(kvc) || !File.Exists(driver)) return false;
        RunKvc(kvc, "reload", driver);
        _loaded = WaitForService(ServiceName);
        return _loaded;
    }

    public bool TryStopAndRemove()
    {
        if (!OperatingSystem.IsWindows()) return false;
        RunSc("stop", ServiceName);
        RunSc("delete", ServiceName);
        _loaded = QueryService(ServiceName);
        return !_loaded;
    }

    public void Dispose()
    {
        // The monitor owns the handle, not the service lifetime. Keep the
        // driver resident so the next launch can reconnect without a reload.
        _loaded = false;
    }

    private static bool QueryService(string name)
    {
        using var process = StartSc("query", name);
        if (!process.WaitForExit(5_000)) return false;
        return process.ExitCode == 0;
    }

    private static bool WaitForService(string name)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (QueryService(name)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    private static void RunSc(params string[] arguments)
    {
        using var process = StartSc(arguments);
        process.WaitForExit(10_000);
    }

    private static void RunKvc(string kvc, string action, string driver)
    {
        // Do not capture or inspect kvc output. The only verification is sc.exe query.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = kvc,
            Arguments = $"driver {action} \"{driver}\"",
            WorkingDirectory = Path.GetDirectoryName(kvc),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        });
        process?.WaitForExit(15_000);
    }

    private static Process StartSc(params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }
}
