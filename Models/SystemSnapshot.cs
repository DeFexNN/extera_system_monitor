namespace ExteraMonitor.Models;

public sealed record SystemSnapshot(
    double CpuUsage,
    double CpuTemperature,
    double MemoryUsage,
    double MemoryTotal,
    double StorageUsage,
    double StorageTotal,
    double DownloadMbps,
    double UploadMbps,
    int ProcessCount,
    TimeSpan Uptime,
    IReadOnlyList<ProcessInfo> Processes,
    IReadOnlyList<DiskMetric> Disks,
    IReadOnlyList<CoreMetric> Cores,
    IReadOnlyList<HardwareSensorMetric> Sensors,
    string TemperatureSource,
    CpuInfoMetric? CpuInfo = null,
    double DiskActivePercent = 0,
    double DiskReadMbps = 0,
    double DiskWriteMbps = 0);

public sealed record ProcessInfo(string Name, string User, double Cpu, double Memory, string Status);
public sealed record DiskMetric(string Name, string VolumeLabel, double UsedGigabytes, double TotalGigabytes, double UsagePercent);
public sealed record CoreMetric(int Index, double UsagePercent);
public sealed record HardwareSensorMetric(string HardwareName, string Name, string Type, double Value, string Unit);
public sealed record CpuInfoMetric(
    string Name,
    string Manufacturer,
    int CoreCount,
    int ThreadCount,
    int CurrentClockMhz,
    int MaxClockMhz,
    long L2CacheKb,
    long L3CacheKb,
    string Architecture = "",
    long L1CacheKb = 0);
public sealed record SoftwareFeature(string Name, string Description, string Status);
