using ExteraMonitor.Models;
namespace ExteraMonitor.Services;
public interface ISystemMetricsProvider { SystemSnapshot GetSnapshot(); }

public static class SystemMetricsProviderFactory
{
    public static ISystemMetricsProvider Create() => OperatingSystem.IsWindows() ? new WindowsSystemMetricsProvider() : new SimulatedMetricsProvider();
}

public sealed class SimulatedMetricsProvider : ISystemMetricsProvider
{
    private readonly Random _random = new(42); private readonly DateTime _started = DateTime.Now.AddHours(-18).AddMinutes(-24); private double _cpu = 34;
    public SystemSnapshot GetSnapshot()
    {
        _cpu = Math.Clamp(_cpu + (_random.NextDouble() - .5) * 8, 12, 78); var memory = Math.Clamp(47 + (_random.NextDouble() - .5) * 5, 35, 68);
        var p = new List<ProcessInfo> { new("extera-monitor", "local", 8.4, 182.4, "Active"), new("dotnet", "local", 5.8, 421.8, "Active"), new("explorer.exe", "system", 2.1, 96.2, "Active"), new("Code.exe", "local", 1.7, 744.1, "Idle") };
        var disks = new List<DiskMetric> { new("C:", "System", 683, 1000, 68.3), new("D:", "Projects", 412, 1000, 41.2) };
        var cores = Enumerable.Range(0, Environment.ProcessorCount).Select(index => new CoreMetric(index, Math.Clamp(_cpu + (_random.NextDouble() - .5) * 15, 0, 100))).ToList();
        return new SystemSnapshot(_cpu, 0, memory, 32, 68.3, 1000, 86.4 + _random.NextDouble() * 8, 18.1 + _random.NextDouble() * 4, 142, DateTime.Now - _started, p, disks, cores, Array.Empty<HardwareSensorMetric>(), "Simulated");
    }
}
