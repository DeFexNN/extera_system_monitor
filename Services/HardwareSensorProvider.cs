using LibreHardwareMonitor.Hardware;
using ExteraMonitor.Models;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExteraMonitor.Services;

public interface IHardwareSensorProvider : IDisposable
{
    HardwareReading Read();
}

public sealed record HardwareReading(double CpuTemperature, IReadOnlyList<HardwareSensorMetric> Sensors, string TemperatureSource);

public sealed class LibreHardwareSensorProvider : IHardwareSensorProvider
{
    private readonly Computer _realtimeComputer = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true
    };
    private readonly Computer _extendedComputer = new()
    {
        IsMotherboardEnabled = true, IsStorageEnabled = true, IsNetworkEnabled = true,
        IsControllerEnabled = true, IsPowerMonitorEnabled = true
    };
    private readonly UpdateVisitor _visitor = new();
    private bool _opened;
    private DateTime _lastExtendedRefresh = DateTime.MinValue;
    private IReadOnlyList<HardwareSensorMetric> _extendedSensors = Array.Empty<HardwareSensorMetric>();
    private static readonly TimeSpan ExtendedRefreshInterval = TimeSpan.FromSeconds(10);

    public HardwareReading Read()
    {
        try
        {
            if (!_opened) { _realtimeComputer.Open(); _extendedComputer.Open(); _opened = true; }
            _realtimeComputer.Accept(_visitor);
            var sampledSensors = Flatten(_realtimeComputer.Hardware)
                .Where(sensor => sensor.Sensor.Value.HasValue)
                .ToList();
            var sensors = sampledSensors.Select(sensor => new HardwareSensorMetric(sensor.HardwareName, sensor.Sensor.Name, sensor.Sensor.SensorType.ToString(), sensor.Sensor.Value!.Value, UnitFor(sensor.Sensor.SensorType))).ToList();
            var now = DateTime.UtcNow;
            if (_extendedSensors.Count == 0 || now - _lastExtendedRefresh >= ExtendedRefreshInterval)
            {
                _extendedComputer.Accept(_visitor);
                _extendedSensors = Flatten(_extendedComputer.Hardware)
                    .Where(sensor => sensor.Sensor.Value.HasValue)
                    .Select(sensor => new HardwareSensorMetric(sensor.HardwareName, sensor.Sensor.Name, sensor.Sensor.SensorType.ToString(), sensor.Sensor.Value!.Value, UnitFor(sensor.Sensor.SensorType)))
                    .ToArray();
                _lastExtendedRefresh = now;
            }
            sensors.AddRange(_extendedSensors);
            var hasGpuLoad = sampledSensors.Any(sensor => sensor.HardwareType.ToString().StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) && sensor.Sensor.SensorType == SensorType.Load);
            var hasGpuTemperature = sampledSensors.Any(sensor => sensor.HardwareType.ToString().StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) && sensor.Sensor.SensorType == SensorType.Temperature);
            if (!hasGpuLoad || !hasGpuTemperature)
                sensors.AddRange(ReadNvidiaDriverSensors(hasGpuLoad, hasGpuTemperature));
            var cpuTemperature = sampledSensors.Where(sensor => sensor.HardwareType == HardwareType.Cpu && sensor.Sensor.SensorType == SensorType.Temperature)
                .OrderByDescending(sensor => sensor.Sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                .Select(sensor => (double)sensor.Sensor.Value!.Value).FirstOrDefault();
            if (cpuTemperature > 0) return new HardwareReading(cpuTemperature, sensors, "Libre Hardware Monitor");
            var fallback = OperatingSystem.IsWindows() ? ReadWmiTemperature() : 0;
            return new HardwareReading(fallback, sensors, fallback > 0 ? "Windows WMI thermal zone" : "No readable hardware sensor");
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            var fallback = OperatingSystem.IsWindows() ? ReadWmiTemperature() : 0;
            return new HardwareReading(fallback, Array.Empty<HardwareSensorMetric>(), fallback > 0 ? "Windows WMI thermal zone" : "No readable hardware sensor");
        }
    }

    public void Dispose()
    {
        if (!_opened) return;
        _realtimeComputer.Close();
        _extendedComputer.Close();
        _opened = false;
    }

    private static IEnumerable<(string HardwareName, HardwareType HardwareType, ISensor Sensor)> Flatten(IEnumerable<IHardware> hardware) => hardware.SelectMany(item => item.Sensors.Select(sensor => (item.Name, item.HardwareType, sensor)).Concat(Flatten(item.SubHardware)));
    private static string UnitFor(SensorType type) => type switch { SensorType.Temperature => "°C", SensorType.Load => "%", SensorType.Clock => "MHz", SensorType.Fan => "RPM", SensorType.Power => "W", SensorType.Current => "A", SensorType.Voltage => "V", SensorType.Factor => "x", SensorType.Data => "GB", SensorType.SmallData => "MB", _ => "" };
    private static IReadOnlyList<HardwareSensorMetric> ReadNvidiaDriverSensors(bool alreadyHasLoad, bool alreadyHasTemperature)
    {
        if (alreadyHasLoad && alreadyHasTemperature) return Array.Empty<HardwareSensorMetric>();
        try
        {
            var executable = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (!File.Exists(executable)) executable = "nvidia-smi.exe";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--query-gpu=name,utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null) return Array.Empty<HardwareSensorMetric>();
            var output = process.StandardOutput.ReadLine();
            if (!process.WaitForExit(1500) || string.IsNullOrWhiteSpace(output)) return Array.Empty<HardwareSensorMetric>();
            var parts = output.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3) return Array.Empty<HardwareSensorMetric>();
            var hardware = parts[0];
            var result = new List<HardwareSensorMetric>(2);
            if (!alreadyHasLoad && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var load))
                result.Add(new HardwareSensorMetric(hardware, "GPU Core (NVAPI)", "Load", load, "%"));
            if (!alreadyHasTemperature && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temperature))
                result.Add(new HardwareSensorMetric(hardware, "GPU Core (NVAPI)", "Temperature", temperature, "°C"));
            return result;
        }
        catch { return Array.Empty<HardwareSensorMetric>(); }
    }
    [SupportedOSPlatform("windows")]
    private static double ReadWmiTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            var values = searcher.Get().Cast<ManagementObject>().Select(item => Convert.ToDouble(item["CurrentTemperature"]) / 10d - 273.15d).Where(value => value is > 0 and < 130).ToList();
            return values.Count == 0 ? 0 : values.Max();
        }
        catch { return 0; }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var child in hardware.SubHardware) child.Accept(this); }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}

public sealed class FallbackHardwareSensorProvider(params IHardwareSensorProvider[] providers) : IHardwareSensorProvider
{
    private readonly IHardwareSensorProvider[] _providers = providers;

    public HardwareReading Read()
    {
        HardwareReading? preferredTemperature = null;
        HardwareReading? latest = null;
        var sensors = new List<HardwareSensorMetric>();
        foreach (var provider in _providers)
        {
            var reading = provider.Read();
            sensors.AddRange(reading.Sensors);
            if (preferredTemperature is null && reading.CpuTemperature > 0)
                preferredTemperature = reading;
            latest = reading;
        }
        var selected = preferredTemperature ?? latest;
        if (selected is null) return new HardwareReading(0, Array.Empty<HardwareSensorMetric>(), "No readable hardware sensor");
        var merged = sensors
            .DistinctBy(sensor => (sensor.HardwareName, sensor.Name, sensor.Type))
            .ToArray();
        return selected with { Sensors = merged };
    }

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
    }
}

public sealed class KernelTemperatureProvider : IHardwareSensorProvider
{
    private const uint IoctlGetTemperature = 0x222004;
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private static readonly IntPtr InvalidHandle = new(-1);
    private readonly KernelDriverLoader _driverLoader = new();
    private IntPtr _handle = InvalidHandle;
    private DriverTelemetryResult? _previousTelemetry;
    private bool _hasReportedSensorRead;

    public KernelTemperatureProvider()
    {
        EnsureHandle();
    }

    public HardwareReading Read()
    {
        EnsureHandle();
        if (!TryReadTelemetry(out var reading) && !TryRead(out reading))
        {
            CloseCurrentHandle();
            _driverLoader.TryReload(automatic: true);
            EnsureHandle();
            if (!TryReadTelemetry(out reading) && !TryRead(out reading))
            {
                DriverDiagnostics.WriteRateLimited("sensor.read.failed",
                    "Could not read CPU temperature after retrying driver startup/device access.", TimeSpan.FromMinutes(1),
                    "Driver startup or sensor read failed after retry. Full driver-startup.log is attached to this chat.", "driver-sensor-failure");
                DriverDiagnostics.QueueLogUpload();
                return new HardwareReading(0, Array.Empty<HardwareSensorMetric>(), "Ryzen temperature driver unavailable");
            }
        }

        return reading;
    }

    public void Dispose()
    {
        CloseCurrentHandle();
        _driverLoader.Dispose();
    }

    private void EnsureHandle()
    {
        if (!OperatingSystem.IsWindows() || (_handle != InvalidHandle && _handle != IntPtr.Zero)) return;
        if (!_driverLoader.EnsureLoaded()) return;
        _handle = CreateFileW("\\\\.\\ExteraMonitorDriver", GenericRead, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (_handle == InvalidHandle || _handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            DriverDiagnostics.WriteRateLimited("device.open.failed",
                $"CreateFileW(\\\\.\\ExteraMonitorDriver) failed; win32={error}; message={new System.ComponentModel.Win32Exception(error).Message}.",
                TimeSpan.FromMinutes(1), "Driver service is running, but opening its device failed (Win32 " + error + "). See local driver log.", "driver-device-open-failed");
            return;
        }
        DriverDiagnostics.WriteOnce("device.open.success", "Opened device \\\\.\\ExteraMonitorDriver successfully.",
            "Driver device opened successfully; querying sensors now.", "driver-device-open");
    }

    private bool TryRead(out HardwareReading reading)
    {
        if (_handle == InvalidHandle || _handle == IntPtr.Zero)
        {
            reading = new HardwareReading(0, Array.Empty<HardwareSensorMetric>(), "Ryzen temperature driver unavailable");
            return false;
        }

        var output = new TemperatureResult();
        var size = Marshal.SizeOf<TemperatureResult>();
        var success = DeviceIoControl(_handle, IoctlGetTemperature, IntPtr.Zero, 0, ref output, size, out var returned, IntPtr.Zero);
        var error = success ? 0 : Marshal.GetLastWin32Error();
        if (!success || returned < size || output.DriverStatus != 0)
        {
            DriverDiagnostics.WriteRateLimited("ioctl.temperature.failed",
                $"Temperature IOCTL failed; win32={error}; returned={returned}/{size}; driverStatus={output.DriverStatus}.",
                TimeSpan.FromMinutes(1), "Driver device opened, but its temperature request failed. See local driver log.", "driver-ioctl-failed");
            reading = new HardwareReading(0, Array.Empty<HardwareSensorMetric>(), "Ryzen temperature driver returned no reading");
            return false;
        }

        var celsius = output.CelsiusMilli / 1000d;
        if (celsius is < -40 or > 150)
        {
            DriverDiagnostics.WriteRateLimited("sensor.temperature.out-of-range",
                $"Temperature IOCTL returned an implausible value; milliCelsius={output.CelsiusMilli}.", TimeSpan.FromMinutes(1));
            reading = new HardwareReading(0, Array.Empty<HardwareSensorMetric>(), "Ryzen temperature driver returned an invalid reading");
            return false;
        }

        var metric = new HardwareSensorMetric("AMD Ryzen 5 7500F", "Tctl/Tdie", "Temperature", celsius, "°C");
        ReportSensorReadOnce("temperature IOCTL", celsius);
        reading = new HardwareReading(celsius, new[] { metric }, "ExteraMonitorDriver kernel driver");
        return true;
    }

    private bool TryReadTelemetry(out HardwareReading reading)
    {
        reading = new HardwareReading(0, Array.Empty<HardwareSensorMetric>(), "Ryzen telemetry driver unavailable");
        if (_handle == InvalidHandle || _handle == IntPtr.Zero) return false;

        var output = new DriverTelemetryResult
        {
            CcdCelsiusMilli = new int[8],
            Cores = Enumerable.Range(0, 64).Select(_ => new DriverCoreTelemetry()).ToArray()
        };
        var size = Marshal.SizeOf<DriverTelemetryResult>();
        var success = DeviceIoControlTelemetry(_handle, IoctlGetTelemetry, IntPtr.Zero, 0, ref output, size, out var returned, IntPtr.Zero);
        var error = success ? 0 : Marshal.GetLastWin32Error();
        if (!success || returned < size || output.DriverStatus != 0)
        {
            DriverDiagnostics.WriteOnce("ioctl.telemetry.unsupported",
                $"Telemetry IOCTL unavailable; win32={error}; returned={returned}/{size}; driverStatus={output.DriverStatus}. Legacy temperature IOCTL will be tried.",
                $"Extended telemetry request unavailable (Win32 {error}, returned {returned}/{size}, driver status {output.DriverStatus}); testing legacy temperature request.",
                "driver-telemetry-unsupported");
            return false;
        }

        const string hardware = "ExteraMonitorDriver / AMD Family 19h";
        var sensors = new List<HardwareSensorMetric>();
        var packageTemperature = output.CelsiusMilli / 1000d;
        if (packageTemperature is < -40 or > 150)
        {
            DriverDiagnostics.WriteRateLimited("sensor.telemetry.temperature.invalid",
                $"Telemetry IOCTL returned invalid package temperature; milliCelsius={output.CelsiusMilli}.", TimeSpan.FromMinutes(1));
            return false;
        }
        sensors.Add(new HardwareSensorMetric(hardware, "Tctl/Tdie", "Temperature", packageTemperature, "°C"));

        var ccdCount = Math.Min(output.CcdCount, (uint)output.CcdCelsiusMilli.Length);
        for (var index = 0; index < ccdCount; index++)
        {
            var value = output.CcdCelsiusMilli[index] / 1000d;
            if (value is > -40 and < 150) sensors.Add(new HardwareSensorMetric(hardware, $"CCD{index + 1} (Tdie)", "Temperature", value, "°C"));
        }

        if ((output.PmCapabilities & 1) != 0)
        {
            AddDriverMetric(sensors, hardware, "CPU PPT", "Power", output.PmPpt, "W", 0, 1000);
            AddDriverMetric(sensors, hardware, "Package (SMU)", "Temperature", output.PmPackageTemperature, "°C", -40, 150);
            AddDriverMetric(sensors, hardware, "Core Power (SMU)", "Power", output.PmCorePower, "W", 0, 500);
            AddDriverMetric(sensors, hardware, "SOC Power (SMU)", "Power", output.PmSocPower, "W", 0, 500);
            AddDriverMetric(sensors, hardware, "Misc Power (SMU)", "Power", output.PmMiscPower, "W", 0, 500);
            AddDriverMetric(sensors, hardware, "Total Power (SMU)", "Power", output.PmTotalPower, "W", 0, 500);
            AddDriverMetric(sensors, hardware, "VDDCR (SMU)", "Voltage", output.PmVddcr, "V", 0, 3);
            AddDriverMetric(sensors, hardware, "TDC (SMU)", "Current", output.PmTdc, "A", 0, 500);
            AddDriverMetric(sensors, hardware, "EDC (SMU)", "Current", output.PmEdc, "A", 0, 500);
            AddDriverMetric(sensors, hardware, "VDDCR SoC (SMU)", "Voltage", output.PmVddcrSoc, "V", 0, 3);
            AddDriverMetric(sensors, hardware, "VDD Misc (SMU)", "Voltage", output.PmVddMisc, "V", 0, 3);
            AddDriverMetric(sensors, hardware, "Fabric (SMU)", "Clock", output.PmFabricClock, "MHz", 0, 10000);
            AddDriverMetric(sensors, hardware, "Uncore (SMU)", "Clock", output.PmUncoreClock, "MHz", 0, 10000);
            AddDriverMetric(sensors, hardware, "Memory (SMU)", "Clock", output.PmMemoryClock, "MHz", 0, 10000);
            AddDriverMetric(sensors, hardware, "IOD Hotspot (SMU)", "Temperature", output.PmIodHotspot, "°C", -40, 200);
            AddDriverMetric(sensors, hardware, "CCD1 (SMU)", "Temperature", output.PmCcd1Temperature, "°C", 1, 200);
            AddDriverMetric(sensors, hardware, "CCD2 (SMU)", "Temperature", output.PmCcd2Temperature, "°C", 1, 200);
            AddDriverMetric(sensors, hardware, "LDO VDD (SMU)", "Voltage", output.PmLdoVdd, "V", 0, 3);
        }

        if (_previousTelemetry is { } previous && output.QueryPerformanceFrequency > 0 && output.QueryPerformanceCounter > previous.QueryPerformanceCounter)
        {
            var seconds = (output.QueryPerformanceCounter - previous.QueryPerformanceCounter) / (double)output.QueryPerformanceFrequency;
            var energyUnit = Math.Pow(0.5, (output.PowerUnitRaw >> 8) & 0x1F);
            var packageEnergy = unchecked(output.PackageEnergy - previous.PackageEnergy);
            if (seconds > 0 && packageEnergy > 0 && packageEnergy < uint.MaxValue / 2d)
                sensors.Add(new HardwareSensorMetric(hardware, "Package (MSR)", "Power", packageEnergy * energyUnit / seconds, "W"));

            var coreCount = Math.Min(output.ProcessorCount, (uint)output.Cores.Length);
            for (var index = 0; index < coreCount; index++)
            {
                var core = output.Cores[index];
                var oldCore = previous.Cores[index];
                var aperfDelta = unchecked(core.Aperf - oldCore.Aperf);
                var mperfDelta = unchecked(core.Mperf - oldCore.Mperf);
                if (core.Valid != 0 && oldCore.Valid != 0 && aperfDelta > 0 && mperfDelta > 0 && aperfDelta < 20_000_000_000 && mperfDelta < 20_000_000_000)
                {
                    sensors.Add(new HardwareSensorMetric(hardware, $"Core #{index + 1} (Effective)", "Clock", aperfDelta / (seconds * 1_000_000d), "MHz"));
                    var coreEnergy = unchecked(core.CoreEnergy - oldCore.CoreEnergy);
                    if (coreEnergy > 0 && coreEnergy < uint.MaxValue / 2d)
                        sensors.Add(new HardwareSensorMetric(hardware, $"Core #{index + 1} (MSR)", "Power", coreEnergy * energyUnit / seconds, "W"));
                }
            }
        }

        _previousTelemetry = output;
        reading = new HardwareReading(packageTemperature, sensors, "ExteraMonitorDriver kernel telemetry");
        ReportSensorReadOnce("telemetry IOCTL", packageTemperature);
        return true;
    }

    private void ReportSensorReadOnce(string source, double celsius)
    {
        if (_hasReportedSensorRead) return;
        _hasReportedSensorRead = true;
        DriverDiagnostics.Write("sensor.read.success", $"First hardware sensor response succeeded via {source}; CPU package={celsius:0.0} °C.",
            $"Driver loaded and sensor reads are working. CPU package: {celsius:0.0} °C.", "driver-sensor-ready");
        DriverDiagnostics.QueueLogUpload();
    }

    private void CloseCurrentHandle()
    {
        if (_handle != IntPtr.Zero && _handle != InvalidHandle) CloseHandle(_handle);
        _handle = InvalidHandle;
    }

    private static void AddDriverMetric(List<HardwareSensorMetric> sensors, string hardware, string name, string type, float value, string unit, double minimum, double maximum)
    {
        if (float.IsFinite(value) && value >= minimum && value <= maximum)
            sensors.Add(new HardwareSensorMetric(hardware, name, type, value, unit));
    }

    [StructLayout(LayoutKind.Sequential)] private struct TemperatureResult
    {
        public int CelsiusMilli;
        public uint RawRegister;
        public uint PciBus;
        public uint PciSlot;
        public int DriverStatus;
    }

    private const uint IoctlGetTelemetry = 0x222008;

    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct DriverCoreTelemetry
    {
        public uint ProcessorIndex;
        public uint Valid;
        public ulong Aperf;
        public ulong Mperf;
        public uint CoreEnergy;
        public uint Pstate;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct DriverTelemetryResult
    {
        public uint Version;
        public uint CpuFamily;
        public uint CpuModel;
        public uint ProcessorCount;
        public uint CcdCount;
        public uint PowerUnitRaw;
        public uint Capabilities;
        public int DriverStatus;
        public long QueryPerformanceCounter;
        public long QueryPerformanceFrequency;
        public uint PackageEnergy;
        public uint PstateStatus;
        public int CelsiusMilli;
        public uint RawRegister;
        public uint PciBus;
        public uint PciSlot;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public int[] CcdCelsiusMilli;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public DriverCoreTelemetry[] Cores;
        public uint SmuStatus;
        public uint PmTableVersion;
        public uint PmTableSize;
        public uint PmCapabilities;
        public float PmPpt;
        public float PmPackageTemperature;
        public float PmCorePower;
        public float PmSocPower;
        public float PmMiscPower;
        public float PmTotalPower;
        public float PmVddcr;
        public float PmTdc;
        public float PmEdc;
        public float PmVddcrSoc;
        public float PmVddMisc;
        public float PmFabricClock;
        public float PmUncoreClock;
        public float PmMemoryClock;
        public float PmIodHotspot;
        public float PmCcd1Temperature;
        public float PmCcd2Temperature;
        public float PmLdoVdd;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint access, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr device, uint code, IntPtr input, int inputSize, ref TemperatureResult output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    private static extern bool DeviceIoControlTelemetry(IntPtr device, uint code, IntPtr input, int inputSize, ref DriverTelemetryResult output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
