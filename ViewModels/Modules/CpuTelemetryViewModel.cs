using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ExteraMonitor.Models;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class CpuTelemetryViewModel : ViewModelBase
{
    private const int HistoryLimit = 900;
    private readonly List<double> _clockHistory = new();
    private readonly List<double> _powerHistory = new();
    private readonly List<double> _voltageHistory = new();
    private readonly Dictionary<string, CpuMetricRowViewModel> _sensorRows = new(StringComparer.OrdinalIgnoreCase);
    private double _peakClock;
    private double _voltageMin = double.NaN;
    private double _voltageMax = double.NaN;

    public ObservableCollection<CpuCoreTelemetryViewModel> Cores { get; } = new();
    public ObservableCollection<CpuMetricRowViewModel> PowerLimits { get; } = new();
    public ObservableCollection<CpuMetricRowViewModel> PowerSummaryRows { get; } = new();
    public ObservableCollection<CpuMetricRowViewModel> TemperatureSummaryRows { get; } = new();
    public ObservableCollection<CpuMetricRowViewModel> FrequencyBands { get; } = new();
    public ObservableCollection<CpuMetricRowViewModel> CStates { get; } = new();
    public ObservableCollection<CpuMetricRowViewModel> AllSensors { get; } = new();
    public ObservableCollection<CpuProcessViewModel> TopProcesses { get; } = new();
    public IReadOnlyList<double> ClockHistory => _clockHistory.ToArray();
    public IReadOnlyList<double> PowerHistory => _powerHistory.ToArray();
    public IReadOnlyList<double> VoltageHistory => _voltageHistory.ToArray();

    public string CurrentClock { get; private set; } = "N/A";
    public string AverageClock { get; private set; } = "N/A";
    public string PeakClock { get; private set; } = "N/A";
    public string PackagePower { get; private set; } = "N/A";
    public string CorePower { get; private set; } = "N/A";
    public string SocPower { get; private set; } = "N/A";
    public string CoreVoltage { get; private set; } = "N/A";
    public string VoltageRange { get; private set; } = "N/A";
    public string PackageTemperature { get; private set; } = "N/A";
    public string CcdTemperature { get; private set; } = "N/A";
    public string CoreTemperature { get; private set; } = "N/A";
    public string CpuName => _cpuName;
    public string CpuInfoLine => string.IsNullOrWhiteSpace(_cpuName) ? "CPU information unavailable" : $"{_cpuName} • {_coreCount} cores / {_threadCount} threads";
    public string CpuPlatformLine => string.Join(" • ", new[] { _architecture, _manufacturer }.Where(item => !string.IsNullOrWhiteSpace(item)));
    public string CpuClockLine => _cpuInfo is null ? "Clock information unavailable" : $"WMI reported {_cpuInfo.CurrentClockMhz:0} MHz current • {_cpuInfo.MaxClockMhz:0} MHz maximum";
    public string CacheLine => _cpuInfo is null ? "Cache information unavailable" : $"L1 {_cpuInfo.L1CacheKb:0} KB • L2 {_cpuInfo.L2CacheKb:0} KB • L3 {_cpuInfo.L3CacheKb:0} KB";
    public string SensorSummary { get; private set; } = "No CPU sensors reported";
    public bool HasClock => _clockHistory.Count > 0;
    public bool HasPower => _powerHistory.Count > 0;
    public bool HasVoltage => _voltageHistory.Count > 0;

    private string _cpuName = "";
    private string _manufacturer = "";
    private string _architecture = "";
    private int _coreCount;
    private int _threadCount;
    private CpuInfoMetric? _cpuInfo;

    public void Update(SystemSnapshot snapshot)
    {
        _cpuInfo = snapshot.CpuInfo;
        _cpuName = snapshot.CpuInfo?.Name ?? "";
        _manufacturer = snapshot.CpuInfo?.Manufacturer ?? "";
        _architecture = snapshot.CpuInfo?.Architecture ?? "";
        _coreCount = snapshot.CpuInfo?.CoreCount ?? snapshot.Cores.Count;
        _threadCount = snapshot.CpuInfo?.ThreadCount ?? snapshot.Cores.Count;

        var sensors = snapshot.Sensors.Where(IsCpuSensor).ToList();
        var allClocks = sensors.Where(sensor => sensor.Type.Equals("Clock", StringComparison.OrdinalIgnoreCase) && sensor.Value > 0).ToList();
        var coreClocks = allClocks.Where(sensor => Has(sensor.Name, "core", "effective") && !Has(sensor.Name, "bus", "fabric", "uncore", "memory")).ToList();
        var clocks = coreClocks.Count == 0 ? allClocks : coreClocks;
        var powers = sensors.Where(sensor => (sensor.Type.Equals("Power", StringComparison.OrdinalIgnoreCase) || sensor.Type.Equals("Current", StringComparison.OrdinalIgnoreCase)) && sensor.Value >= 0).ToList();
        var voltages = sensors.Where(sensor => sensor.Type.Equals("Voltage", StringComparison.OrdinalIgnoreCase) && sensor.Value > 0).ToList();
        var temperatures = sensors.Where(sensor => sensor.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase) && sensor.Value > -40 && sensor.Value < 150).ToList();
        UpdateSensorRows(sensors);

        var currentClock = clocks.Count == 0 ? 0 : clocks.Average(sensor => sensor.Value);
        _peakClock = Math.Max(_peakClock, clocks.Count == 0 ? 0 : clocks.Max(sensor => sensor.Value));
        AddSample(_clockHistory, currentClock);
        CurrentClock = FormatFrequency(currentClock);
        AverageClock = FormatFrequency(_clockHistory.Count == 0 ? 0 : _clockHistory.Where(value => value > 0).DefaultIfEmpty().Average());
        PeakClock = FormatFrequency(_peakClock);

        var packagePower = FindPower(powers, "package", "cpu", "socket");
        var corePower = FindPower(powers, "core");
        var socPower = FindPower(powers, "soc", "uncore");
        AddSample(_powerHistory, packagePower);
        PackagePower = FormatWatts(packagePower);
        CorePower = FormatWatts(corePower);
        SocPower = FormatWatts(socPower);
        SetRows(PowerSummaryRows,
            new CpuMetricRowViewModel("Package", PackagePower),
            new CpuMetricRowViewModel("Core", CorePower),
            new CpuMetricRowViewModel("SoC", SocPower));

        var coreVoltage = FindVoltage(voltages);
        AddSample(_voltageHistory, coreVoltage);
        if (coreVoltage > 0)
        {
            _voltageMin = double.IsNaN(_voltageMin) ? coreVoltage : Math.Min(_voltageMin, coreVoltage);
            _voltageMax = double.IsNaN(_voltageMax) ? coreVoltage : Math.Max(_voltageMax, coreVoltage);
        }
        CoreVoltage = FormatVoltage(coreVoltage);
        VoltageRange = double.IsNaN(_voltageMin) ? "N/A" : $"min {_voltageMin:0.000} • max {_voltageMax:0.000} V";

        PackageTemperature = snapshot.CpuTemperature > 0 ? $"{snapshot.CpuTemperature:0.0} °C" : "N/A";
        var ccd = temperatures.Where(sensor => Has(sensor.Name, "ccd")).Select(sensor => sensor.Value).ToArray();
        var coreTemps = temperatures.Where(sensor => Has(sensor.Name, "core") && !Has(sensor.Name, "package")).Select(sensor => sensor.Value).ToArray();
        CcdTemperature = ccd.Length == 0 ? "N/A" : $"avg {ccd.Average():0.0} °C • max {ccd.Max():0.0} °C";
        CoreTemperature = coreTemps.Length == 0 ? "N/A" : $"avg {coreTemps.Average():0.0} °C • max {coreTemps.Max():0.0} °C";
        var packageSensor = temperatures.FirstOrDefault(sensor => Has(sensor.Name, "package", "tctl", "tdie"));
        SetRows(TemperatureSummaryRows,
            new CpuMetricRowViewModel("Package / Tctl", packageSensor is null ? "N/A" : $"{packageSensor.Value:0.0} °C"),
            new CpuMetricRowViewModel("CCD", ccd.Length == 0 ? "N/A" : $"{ccd.Max():0.0} °C"),
            new CpuMetricRowViewModel("Core (avg)", coreTemps.Length == 0 ? "N/A" : $"{coreTemps.Average():0.0} °C"),
            new CpuMetricRowViewModel("Core (max)", coreTemps.Length == 0 ? "N/A" : $"{coreTemps.Max():0.0} °C"));

        UpdateCores(snapshot.Cores, clocks, coreTemps);
        UpdateFrequencyBands(clocks);
        UpdatePowerLimits(powers);
        UpdateCStates(sensors);
        UpdateProcesses(snapshot.Processes);
        SensorSummary = sensors.Count == 0 ? "No CPU sensors reported" : $"{sensors.Count} CPU sensors • source: {snapshot.TemperatureSource}";

        OnPropertyChanged(nameof(CpuInfoLine)); OnPropertyChanged(nameof(CpuPlatformLine)); OnPropertyChanged(nameof(CpuClockLine)); OnPropertyChanged(nameof(CacheLine));
        OnPropertyChanged(nameof(CurrentClock)); OnPropertyChanged(nameof(AverageClock)); OnPropertyChanged(nameof(PeakClock)); OnPropertyChanged(nameof(PackagePower)); OnPropertyChanged(nameof(CorePower)); OnPropertyChanged(nameof(SocPower));
        OnPropertyChanged(nameof(CoreVoltage)); OnPropertyChanged(nameof(VoltageRange)); OnPropertyChanged(nameof(PackageTemperature)); OnPropertyChanged(nameof(CcdTemperature)); OnPropertyChanged(nameof(CoreTemperature)); OnPropertyChanged(nameof(SensorSummary));
        OnPropertyChanged(nameof(HasClock)); OnPropertyChanged(nameof(HasPower)); OnPropertyChanged(nameof(HasVoltage)); OnPropertyChanged(nameof(ClockHistory)); OnPropertyChanged(nameof(PowerHistory)); OnPropertyChanged(nameof(VoltageHistory));
    }

    private void UpdateSensorRows(IReadOnlyList<HardwareSensorMetric> sensors)
    {
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sensor in sensors.OrderBy(sensor => sensor.Type).ThenBy(sensor => sensor.Name))
        {
            var key = $"{sensor.Type}|{sensor.Name}";
            activeKeys.Add(key);
            var value = $"{sensor.Value:0.###} {sensor.Unit}".Trim();
            if (_sensorRows.TryGetValue(key, out var row)) row.Update(sensor.Name, value, sensor.Type);
            else
            {
                row = new CpuMetricRowViewModel(sensor.Name, value, sensor.Type);
                _sensorRows[key] = row;
                AllSensors.Add(row);
            }
        }
        foreach (var key in _sensorRows.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            AllSensors.Remove(_sensorRows[key]);
            _sensorRows.Remove(key);
        }
        SensorSummary = sensors.Count == 0 ? "No CPU sensors reported" : $"{sensors.Count} CPU sensors • source: {sensors.FirstOrDefault()?.HardwareName}";
    }

    private void UpdateProcesses(IReadOnlyList<ProcessInfo> processes)
    {
        var current = processes.OrderByDescending(item => item.Cpu).Take(5).ToArray();
        for (var index = 0; index < current.Length; index++)
        {
            var item = current[index];
            if (index < TopProcesses.Count && TopProcesses[index].Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                TopProcesses[index].Update(item);
            else if (index < TopProcesses.Count)
                TopProcesses[index] = new CpuProcessViewModel(item);
            else
                TopProcesses.Add(new CpuProcessViewModel(item));
        }
        while (TopProcesses.Count > current.Length) TopProcesses.RemoveAt(TopProcesses.Count - 1);
    }

    private static void SetRows(ObservableCollection<CpuMetricRowViewModel> target, params CpuMetricRowViewModel[] rows)
    {
        while (target.Count > rows.Length) target.RemoveAt(target.Count - 1);
        for (var index = 0; index < rows.Length; index++)
        {
            if (index < target.Count) target[index].Update(rows[index].Label, rows[index].Value, rows[index].Detail, rows[index].Percent);
            else target.Add(rows[index]);
        }
    }

    private void UpdateCores(IReadOnlyList<CoreMetric> loads, IReadOnlyList<HardwareSensorMetric> clocks, IReadOnlyList<double> temperatures)
    {
        for (var index = 0; index < loads.Count; index++)
        {
            var load = loads[index];
            var clock = clocks.Where(sensor => CoreNumber(sensor.Name) == load.Index).Select(sensor => sensor.Value).DefaultIfEmpty().Average();
            var temp = temperatures.ElementAtOrDefault(load.Index - 1);
            if (index < Cores.Count && Cores[index].Index == load.Index) Cores[index].Update(load.UsagePercent, clock, temp);
            else if (index < Cores.Count) Cores[index] = new CpuCoreTelemetryViewModel(load.Index, load.UsagePercent, clock, temp);
            else Cores.Add(new CpuCoreTelemetryViewModel(load.Index, load.UsagePercent, clock, temp));
        }
        while (Cores.Count > loads.Count) Cores.RemoveAt(Cores.Count - 1);
    }

    private void UpdateFrequencyBands(IReadOnlyList<HardwareSensorMetric> clocks)
    {
        FrequencyBands.Clear();
        if (clocks.Count == 0)
        {
            FrequencyBands.Add(CpuMetricRowViewModel.Unavailable("Frequency distribution"));
            return;
        }
        var bands = new[] { ("0–2 GHz", 0d, 2000d), ("2–4 GHz", 2000d, 4000d), ("4–5 GHz", 4000d, 5000d), (">5 GHz", 5000d, double.MaxValue) };
        foreach (var band in bands)
        {
            var percent = clocks.Count(value => value.Value >= band.Item2 && value.Value < band.Item3) * 100d / clocks.Count;
            FrequencyBands.Add(new CpuMetricRowViewModel(band.Item1, $"{percent:0}%", "of sampled cores", percent));
        }
    }

    private void UpdatePowerLimits(IReadOnlyList<HardwareSensorMetric> powers)
    {
        PowerLimits.Clear();
        foreach (var label in new[] { "PPT", "TDC", "EDC" })
        {
            var sensor = powers.FirstOrDefault(item => Has(item.Name, label));
            PowerLimits.Add(sensor is null ? CpuMetricRowViewModel.Unavailable(label) : new CpuMetricRowViewModel(label, $"{sensor.Value:0.0} {sensor.Unit}", sensor.Unit.Contains('%') ? "reported percentage" : sensor.Name, sensor.Unit.Contains('%') ? Math.Clamp(sensor.Value, 0, 100) : 0));
        }
    }

    private void UpdateCStates(IReadOnlyList<HardwareSensorMetric> sensors)
    {
        CStates.Clear();
        var states = sensors.Where(item => item.Type.Equals("Load", StringComparison.OrdinalIgnoreCase) && Has(item.Name, "c0", "c1", "c2", "c3", "c6", "c7")).ToList();
        if (states.Count == 0) { CStates.Add(CpuMetricRowViewModel.Unavailable("C-state residency")); return; }
        foreach (var state in states) CStates.Add(new CpuMetricRowViewModel(state.Name, $"{state.Value:0.0}%", "reported by sensor", Math.Clamp(state.Value, 0, 100)));
    }

    private static bool IsCpuSensor(HardwareSensorMetric sensor)
    {
        var text = $"{sensor.HardwareName} {sensor.Name}";
        if (Has(text, "gpu", "graphics", "radeon", "geforce", "storage", "nvme", "motherboard", "network")) return false;
        return Has(text, "cpu", "processor", "ryzen", "core", "package", "tdie", "tctl", "vddcr", "ppt", "tdc", "edc", "soc", "fabric", "uncore", "memory", "iod", "power", "voltage", "clock") || sensor.Name.Equals("Tctl/Tdie", StringComparison.OrdinalIgnoreCase);
    }

    private static double FindPower(IReadOnlyList<HardwareSensorMetric> sensors, params string[] keys) => sensors.Where(sensor => keys.Any(key => Has(sensor.Name, key))).OrderByDescending(sensor => Has(sensor.Name, "package", "socket")).Select(sensor => sensor.Value).FirstOrDefault();
    private static double FindVoltage(IReadOnlyList<HardwareSensorMetric> sensors) => sensors.Where(sensor => Has(sensor.Name, "core", "cpu", "vddcr", "sv12")).Select(sensor => sensor.Value).FirstOrDefault();
    private static int CoreNumber(string name)
    {
        var match = Regex.Match(name, @"(?:core|thread|cpu)\s*#?\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value + (name.Contains("#") ? 0 : 1) : -1;
    }
    private static bool Has(string value, params string[] keys) => keys.Any(key => value.Contains(key, StringComparison.OrdinalIgnoreCase));
    private static string FormatFrequency(double value) => value > 0 ? $"{value / 1000d:0.00} GHz" : "N/A";
    private static string FormatWatts(double value) => value > 0 ? $"{value:0.0} W" : "N/A";
    private static string FormatVoltage(double value) => value > 0 ? $"{value:0.000} V" : "N/A";
    private static void AddSample(List<double> values, double value)
    {
        if (!double.IsFinite(value) || value <= 0) return;
        values.Add(value);
        while (values.Count > HistoryLimit) values.RemoveAt(0);
    }
}

public sealed class CpuMetricRowViewModel : ViewModelBase
{
    private string _label;
    private string _value;
    private string _detail;
    private double _percent;
    public string Label { get => _label; private set => SetProperty(ref _label, value); }
    public string Value { get => _value; private set => SetProperty(ref _value, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public double Percent { get => _percent; private set => SetProperty(ref _percent, value); }
    public bool HasProgress => Percent > 0 || Detail is "of sampled cores" or "reported by sensor" or "reported percentage";
    public bool IsAvailable => Value != "N/A";
    public CpuMetricRowViewModel(string label, string value, string detail = "", double percent = 0) { _label = label; _value = value; _detail = detail; _percent = percent; }
    public void Update(string label, string value, string detail = "", double percent = 0) { Label = label; Value = value; Detail = detail; Percent = percent; OnPropertyChanged(nameof(HasProgress)); }
    public static CpuMetricRowViewModel Unavailable(string label) => new(label, "N/A", "sensor not exposed");
}

public sealed class CpuCoreTelemetryViewModel : ViewModelBase
{
    private double _usage;
    private double _clock;
    private double _temperature;
    public int Index { get; }
    public string Label => $"Core {Index:00}";
    public double Usage { get => _usage; private set { _usage = value; OnPropertyChanged(nameof(Load)); OnPropertyChanged(nameof(UsagePercent)); } }
    public double UsagePercent => Usage;
    public string Load => $"{Usage:0.0}%";
    public string Clock => _clock > 0 ? $"{_clock / 1000d:0.00} GHz" : "N/A";
    public string Temperature => _temperature > 0 ? $"{_temperature:0.0} °C" : "N/A";
    public CpuCoreTelemetryViewModel(int index, double load, double clock, double temperature) { Index = index; Update(load, clock, temperature); }
    public void Update(double load, double clock, double temperature) { Usage = load; _clock = clock; _temperature = temperature; OnPropertyChanged(nameof(Clock)); OnPropertyChanged(nameof(Temperature)); }
}

public sealed class CpuProcessViewModel(ProcessInfo process) : ViewModelBase
{
    private double _cpu = process.Cpu;
    public string Name { get; } = process.Name;
    public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public string CpuText => $"{Cpu:0.0}%";
    public void Update(ProcessInfo process) { Cpu = process.Cpu; OnPropertyChanged(nameof(CpuText)); }
}
