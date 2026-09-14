using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using ExteraMonitor.Models;

namespace ExteraMonitor.Services;

/// <summary>Samples Windows and .NET counters without external packages or administrative privileges.</summary>
public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider, IDisposable
{
    private readonly Dictionary<int, ProcessSample> _processSamples = new();
    private CpuTimes? _previousCpuTimes;
    private NetworkSample? _previousNetworkSample;
    private CoreTimes[] _previousCoreTimes = [];
    private Task<IReadOnlyList<DiskMetric>>? _diskReadTask;
    private IReadOnlyList<DiskMetric> _lastDisks = Array.Empty<DiskMetric>();
    private Task<DiskPerformanceSample>? _diskPerformanceTask;
    private DiskPerformanceSample _lastDiskPerformance;
    private readonly IHardwareSensorProvider _hardwareSensors = new FallbackHardwareSensorProvider(
        new KernelTemperatureProvider(),
        new LibreHardwareSensorProvider());
    private CpuInfoMetric? _cpuInfo;

    public SystemSnapshot GetSnapshot()
    {
        // WMI can be slow on some firmware; this provider is called from the
        // background refresh loop, so never perform that query on the UI thread.
        _cpuInfo ??= ReadCpuInfo();
        var memory = ReadMemory(); var cores = ReadCoreUsage(); var hardware = _hardwareSensors.Read();
        // A stalled/removable volume must never block CPU, memory, network or sensors.
        var disks = ReadDisksInBackground();
        var diskPerformance = ReadDiskPerformanceInBackground();
        var (download, upload) = ReadNetwork();
        var processes = ReadProcesses(out var processCount);
        var primary = disks.FirstOrDefault() ?? new DiskMetric("—", "No fixed disk", 0, 0, 0);

        return new SystemSnapshot(
            cores.Count == 0 ? ReadCpuUsage() : cores.Average(core => core.UsagePercent), hardware.CpuTemperature, memory.UsagePercent, memory.TotalGigabytes,
            primary.UsagePercent, primary.TotalGigabytes, download, upload,
            processCount, TimeSpan.FromMilliseconds(Environment.TickCount64), processes, disks, cores, hardware.Sensors, hardware.TemperatureSource, _cpuInfo,
            diskPerformance.ActivePercent, diskPerformance.ReadMbps, diskPerformance.WriteMbps);
    }

    public void Dispose() => _hardwareSensors.Dispose();

    private IReadOnlyList<CoreMetric> ReadCoreUsage()
    {
        var itemSize = Marshal.SizeOf<ProcessorPerformanceInformation>();
        var buffer = Marshal.AllocHGlobal(itemSize * Math.Max(1, Environment.ProcessorCount));
        try
        {
            var status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, itemSize * Environment.ProcessorCount, out var returnedLength);
            if (status != 0 || returnedLength < itemSize) return [];
            var count = returnedLength / itemSize; var current = new CoreTimes[count]; var result = new List<CoreMetric>(count);
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<ProcessorPerformanceInformation>(IntPtr.Add(buffer, index * itemSize));
                current[index] = new CoreTimes(info.IdleTime, info.KernelTime, info.UserTime);
                var usage = 0d;
                if (_previousCoreTimes.Length == count)
                {
                    var previous = _previousCoreTimes[index]; var total = (info.KernelTime - previous.Kernel) + (info.UserTime - previous.User);
                    if (total > 0) usage = Math.Clamp((total - (info.IdleTime - previous.Idle)) * 100d / total, 0, 100);
                }
                result.Add(new CoreMetric(index + 1, usage));
            }
            _previousCoreTimes = current; return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var current = new CpuTimes(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
        if (_previousCpuTimes is not { } previous) { _previousCpuTimes = current; return 0; }
        _previousCpuTimes = current;
        var total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total == 0) return 0;
        var busy = total - (current.Idle - previous.Idle);
        return Math.Clamp(busy * 100d / total, 0, 100);
    }

    private static MemorySample ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return new MemorySample(0, 0);
        var total = status.TotalPhysical / Gigabyte;
        var used = (status.TotalPhysical - status.AvailablePhysical) / (double)Gigabyte;
        return new MemorySample(total, Math.Clamp(used * 100 / total, 0, 100));
    }

    private IReadOnlyList<DiskMetric> ReadDisksInBackground()
    {
        var task = Volatile.Read(ref _diskReadTask);
        if (task is null)
        {
            var started = Task.Run(ReadDisksCore);
            task = Interlocked.CompareExchange(ref _diskReadTask, started, null) ?? started;
        }

        if (!task.IsCompleted) return _lastDisks;

        try { _lastDisks = task.GetAwaiter().GetResult(); }
        catch { /* Keep the last successful disk sample. */ }
        finally { Interlocked.CompareExchange(ref _diskReadTask, null, task); }
        return _lastDisks;
    }

    private static IReadOnlyList<DiskMetric> ReadDisksCore() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
        .OrderBy(drive => drive.Name)
        .Take(3)
        .Select(drive =>
        {
            var total = drive.TotalSize / (double)Gigabyte;
            var used = (drive.TotalSize - drive.AvailableFreeSpace) / (double)Gigabyte;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local volume" : drive.VolumeLabel;
            return new DiskMetric(drive.Name.TrimEnd('\\'), label, used, total, total == 0 ? 0 : used * 100 / total);
        })
        .ToList();

    private DiskPerformanceSample ReadDiskPerformanceInBackground()
    {
        if (!OperatingSystem.IsWindows()) return default;
        var task = Volatile.Read(ref _diskPerformanceTask);
        if (task is null)
        {
            var started = Task.Run(ReadDiskPerformanceCore);
            task = Interlocked.CompareExchange(ref _diskPerformanceTask, started, null) ?? started;
        }

        if (!task.IsCompleted) return _lastDiskPerformance;

        try { _lastDiskPerformance = task.GetAwaiter().GetResult(); }
        catch { /* Keep the last successful driver counter sample. */ }
        finally { Interlocked.CompareExchange(ref _diskPerformanceTask, null, task); }
        return _lastDiskPerformance;
    }

    [SupportedOSPlatform("windows")]
    private static DiskPerformanceSample ReadDiskPerformanceCore()
    {
        if (!OperatingSystem.IsWindows()) return default;
        using var searcher = new System.Management.ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, PercentDiskTime, DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
        using var results = searcher.Get();
        var rows = results.Cast<System.Management.ManagementObject>().ToArray();
        var row = rows.FirstOrDefault(item => string.Equals(Convert.ToString(item["Name"]), "_Total", StringComparison.OrdinalIgnoreCase))
                  ?? rows.FirstOrDefault();
        if (row is null) return default;
        static double ReadCounter(System.Management.ManagementBaseObject source, string property) =>
            double.TryParse(Convert.ToString(source[property], System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        return new DiskPerformanceSample(
            Math.Clamp(ReadCounter(row, "PercentDiskTime"), 0, 100),
            Math.Max(0, ReadCounter(row, "DiskReadBytesPersec") / 1_000_000d),
            Math.Max(0, ReadCounter(row, "DiskWriteBytesPersec") / 1_000_000d));
    }

    private (double DownloadMbps, double UploadMbps) ReadNetwork()
    {
        var bytesReceived = 0L; var bytesSent = 0L;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel))
        {
            var statistics = network.GetIPStatistics(); bytesReceived += statistics.BytesReceived; bytesSent += statistics.BytesSent;
        }
        var current = new NetworkSample(bytesReceived, bytesSent, Stopwatch.GetTimestamp());
        if (_previousNetworkSample is not { } previous) { _previousNetworkSample = current; return (0, 0); }
        _previousNetworkSample = current;
        var seconds = (current.Timestamp - previous.Timestamp) / (double)Stopwatch.Frequency;
        if (seconds <= 0) return (0, 0);
        return (Math.Max(0, (current.Received - previous.Received) * 8d / seconds / 1_000_000), Math.Max(0, (current.Sent - previous.Sent) * 8d / seconds / 1_000_000));
    }

    private IReadOnlyList<ProcessInfo> ReadProcesses(out int count)
    {
        var now = DateTime.UtcNow; var result = new List<ProcessInfo>(); var processes = Process.GetProcesses(); count = processes.Length;
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var totalCpu = process.TotalProcessorTime; var cpu = 0d;
                    if (_processSamples.TryGetValue(process.Id, out var previous))
                    {
                        var elapsed = (now - previous.SampledAt).TotalMilliseconds;
                        if (elapsed > 0) cpu = Math.Clamp((totalCpu - previous.TotalCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
                    }
                    _processSamples[process.Id] = new ProcessSample(totalCpu, now);
                    result.Add(new ProcessInfo(process.ProcessName, "local", cpu, process.WorkingSet64 / 1_048_576d, "Running"));
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return result.OrderByDescending(process => process.Cpu).ThenByDescending(process => process.Memory).Take(8).ToList();
    }

    private static CpuInfoMetric? ReadCpuInfo()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, CurrentClockSpeed, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor");
            using var results = searcher.Get();
            var processor = results.Cast<System.Management.ManagementObject>().FirstOrDefault();
            if (processor is null) return null;
            static int ReadInt(System.Management.ManagementBaseObject source, string property) =>
                int.TryParse(Convert.ToString(source[property]), out var value) ? value : 0;
            static long ReadLong(System.Management.ManagementBaseObject source, string property) =>
                long.TryParse(Convert.ToString(source[property]), out var value) ? value : 0;
            var identity = ReadCpuIdentity();
            return new CpuInfoMetric(
                Convert.ToString(processor["Name"])?.Trim() ?? "Unknown processor",
                Convert.ToString(processor["Manufacturer"])?.Trim() ?? "Unknown manufacturer",
                ReadInt(processor, "NumberOfCores"),
                ReadInt(processor, "NumberOfLogicalProcessors"),
                ReadInt(processor, "CurrentClockSpeed"),
                ReadInt(processor, "MaxClockSpeed"),
                ReadLong(processor, "L2CacheSize"),
                ReadLong(processor, "L3CacheSize"),
                identity.Architecture,
                identity.L1CacheKb);
        }
        catch
        {
            return null;
        }
    }

    private static (string Architecture, long L1CacheKb) ReadCpuIdentity()
    {
        if (!X86Base.IsSupported) return ("", 0);
        try
        {
            var basic = X86Base.CpuId(1, 0);
            var eax = basic.Eax;
            var baseFamily = (eax >> 8) & 0xF;
            var extendedFamily = (eax >> 20) & 0xFF;
            var family = baseFamily == 0xF ? baseFamily + extendedFamily : baseFamily;
            var baseModel = (eax >> 4) & 0xF;
            var extendedModel = (eax >> 16) & 0xF;
            var model = baseFamily is 0x6 or 0xF ? baseModel | (extendedModel << 4) : baseModel;
            var architecture = $"x86 family {family:X}, model {model:X}";

            var maxExtended = X86Base.CpuId(unchecked((int)0x80000000), 0).Eax;
            long l1CacheKb = 0;
            if (unchecked((uint)maxExtended) >= 0x80000005)
            {
                var cache = X86Base.CpuId(unchecked((int)0x80000005), 0);
                l1CacheKb = ((cache.Ecx >> 24) & 0xFF) + ((cache.Edx >> 24) & 0xFF);
            }
            return (architecture, l1CacheKb);
        }
        catch (PlatformNotSupportedException)
        {
            return ("", 0);
        }
    }

    private const ulong Gigabyte = 1_073_741_824;
    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
    private readonly record struct CoreTimes(long Idle, long Kernel, long User);
    private readonly record struct NetworkSample(long Received, long Sent, long Timestamp);
    private readonly record struct ProcessSample(TimeSpan TotalCpu, DateTime SampledAt);
    private readonly record struct MemorySample(double TotalGigabytes, double UsagePercent);
    private readonly record struct DiskPerformanceSample(double ActivePercent, double ReadMbps, double WriteMbps);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MemoryStatus
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical; public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low; public uint High; public ulong ToUInt64() => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessorPerformanceInformation { public long IdleTime; public long KernelTime; public long UserTime; public long DpcTime; public long InterruptTime; public uint InterruptCount; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
    private const int SystemProcessorPerformanceInformation = 8;
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);
}
