using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media;
using ExteraMonitor.Models;
using ExteraMonitor.Services;
using Microsoft.Win32;
namespace ExteraMonitor.ViewModels.Modules;
public abstract class MonitorModuleViewModel : ViewModelBase { public string ModuleName { get; } protected MonitorModuleViewModel(string name) => ModuleName = name; public abstract void Update(SystemSnapshot snapshot); }

public sealed class ProcessSampleRowViewModel : ViewModelBase
{
    private double _cpu, _memory;
    private string _status;
    public string Name { get; }
    public string User { get; }
    public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public double Memory { get => _memory; private set => SetProperty(ref _memory, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Key => $"{Name}\u001f{User}";
    public ProcessSampleRowViewModel(ProcessInfo process) { Name = process.Name; User = process.User; _status = process.Status; Update(process); }
    public void Update(ProcessInfo process) { Cpu = process.Cpu; Memory = process.Memory; Status = process.Status; }
    public static IReadOnlyList<ProcessSampleRowViewModel> Reconcile(IEnumerable<ProcessInfo> source, IEnumerable<ProcessSampleRowViewModel> existing)
    {
        var queues = existing.GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => new Queue<ProcessSampleRowViewModel>(group), StringComparer.OrdinalIgnoreCase);
        var result = new List<ProcessSampleRowViewModel>();
        foreach (var process in source)
        {
            var key = $"{process.Name}\u001f{process.User}";
            if (queues.TryGetValue(key, out var queue) && queue.Count > 0) { var row = queue.Dequeue(); row.Update(process); result.Add(row); }
            else result.Add(new ProcessSampleRowViewModel(process));
        }
        return result;
    }
}

public static class ObservableCollectionReconciler
{
    public static void SetItems<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (var index = target.Count - 1; index >= 0; index--)
            if (!desired.Any(item => ReferenceEquals(item, target[index]))) target.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], desired[index])) continue;
            var existingIndex = target.IndexOf(desired[index]);
            if (existingIndex >= 0) target.Move(existingIndex, index);
            else target.Insert(index, desired[index]);
        }
    }
}

public sealed class StorageVolumeViewModel : ViewModelBase
{
    private string _volumeLabel;
    private double _used, _total, _usage;
    public string Name { get; }
    public string VolumeLabel { get => _volumeLabel; private set => SetProperty(ref _volumeLabel, value); }
    public double UsedGigabytes { get => _used; private set => SetProperty(ref _used, value); }
    public double TotalGigabytes { get => _total; private set => SetProperty(ref _total, value); }
    public double UsagePercent { get => _usage; private set => SetProperty(ref _usage, value); }
    public StorageVolumeViewModel(DiskMetric disk) { Name = disk.Name; _volumeLabel = disk.VolumeLabel; Update(disk); }
    public void Update(DiskMetric disk) { VolumeLabel = disk.VolumeLabel; UsedGigabytes = disk.UsedGigabytes; TotalGigabytes = disk.TotalGigabytes; UsagePercent = disk.UsagePercent; }
}

public sealed class SensorReadingViewModel : ViewModelBase
{
    private string _hardwareName, _name, _type, _unit;
    private double _value;
    public string HardwareName { get => _hardwareName; private set => SetProperty(ref _hardwareName, value); }
    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public string Type { get => _type; private set => SetProperty(ref _type, value); }
    public double Value { get => _value; private set => SetProperty(ref _value, value); }
    public string Unit { get => _unit; private set => SetProperty(ref _unit, value); }
    public string Key => $"{HardwareName}\u001f{Type}\u001f{Name}";
    public SensorReadingViewModel(HardwareSensorMetric sensor) { _hardwareName = sensor.HardwareName; _name = sensor.Name; _type = sensor.Type; _unit = sensor.Unit; _value = sensor.Value; }
    public void Update(HardwareSensorMetric sensor) { HardwareName = sensor.HardwareName; Name = sensor.Name; Type = sensor.Type; Value = sensor.Value; Unit = sensor.Unit; }
}
public sealed class OverviewModuleViewModel : MonitorModuleViewModel
{
    private const int HistoryLimit = 30;
    private readonly List<double> _cpuHistory = new(), _memoryHistory = new(), _storageHistory = [];
    private double _cpu, _memory, _storage, _download, _upload, _memoryTotal, _storageTotal;
    private int _threadCount, _sensorCount, _processCount, _networkSamples;
    private string _uptime = "—", _primaryVolume = "No ready volume";
    public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public double Memory { get => _memory; private set => SetProperty(ref _memory, value); }
    public double Storage { get => _storage; private set => SetProperty(ref _storage, value); }
    public double Download { get => _download; private set => SetProperty(ref _download, value); }
    public double Upload { get => _upload; private set => SetProperty(ref _upload, value); }
    public double MemoryTotal { get => _memoryTotal; private set => SetProperty(ref _memoryTotal, value); }
    public double StorageTotal { get => _storageTotal; private set => SetProperty(ref _storageTotal, value); }
    public int ThreadCount { get => _threadCount; private set => SetProperty(ref _threadCount, value); }
    public int SensorCount { get => _sensorCount; private set => SetProperty(ref _sensorCount, value); }
    public int ProcessCount { get => _processCount; private set => SetProperty(ref _processCount, value); }
    public string Uptime { get => _uptime; private set => SetProperty(ref _uptime, value); }
    public string PrimaryVolume { get => _primaryVolume; private set => SetProperty(ref _primaryVolume, value); }
    public string Cores => $"{ThreadCount} logical processors";
    public string CpuTemperature { get; private set; } = "N/A";
    public string MemoryLabel => MemoryTotal > 0 ? $"{Memory:0.0}%" : "N/A";
    public bool HasMemoryData => MemoryTotal > 0;
    public string MemoryCapacityLabel => MemoryTotal > 0 ? $"{MemoryTotal:0.0} GB" : "N/A";
    public string MemoryUsed => MemoryTotal > 0 ? $"{MemoryTotal * Memory / 100:0.0} GB" : "N/A";
    public string MemoryDetail => MemoryTotal > 0 ? $"{MemoryTotal * Memory / 100:0.0} GB of {MemoryTotal:0.0} GB RAM" : "Capacity unavailable";
    public string StorageUsed => StorageTotal > 0 ? $"{StorageTotal * Storage / 100:0.0} GB" : "N/A";
    public bool HasStorageData => StorageTotal > 0;
    public string NetworkSummary => _networkSamples > 1 ? $"↓ {Download:0.0}  ·  ↑ {Upload:0.0} Mbps" : "Collecting network counter baseline…";
    public IReadOnlyList<double> CpuHistory => _cpuHistory.ToArray();
    public IReadOnlyList<double> MemoryHistory => _memoryHistory.ToArray();
    public IReadOnlyList<double> StorageHistory => _storageHistory.ToArray();
    public OverviewModuleViewModel() : base("Overview") { }

    public override void Update(SystemSnapshot snapshot)
    {
        Cpu = snapshot.CpuUsage; Memory = snapshot.MemoryUsage; Storage = snapshot.StorageUsage;
        Download = snapshot.DownloadMbps; Upload = snapshot.UploadMbps; MemoryTotal = snapshot.MemoryTotal; StorageTotal = snapshot.StorageTotal; _networkSamples++;
        ThreadCount = snapshot.CpuInfo?.ThreadCount ?? (snapshot.Cores.Count > 0 ? snapshot.Cores.Count : Environment.ProcessorCount);
        SensorCount = snapshot.Sensors.Count; ProcessCount = snapshot.ProcessCount;
        Uptime = $"{(int)snapshot.Uptime.TotalHours}h {snapshot.Uptime.Minutes:00}m";
        var disk = snapshot.Disks.FirstOrDefault(); PrimaryVolume = disk is null ? "No ready volume" : $"{disk.Name} · {disk.VolumeLabel}";
        CpuTemperature = snapshot.CpuTemperature > 0 ? $"{snapshot.CpuTemperature:0.0} °C" : "N/A";
        Append(_cpuHistory, Cpu); if (snapshot.MemoryTotal > 0) Append(_memoryHistory, Memory); if (snapshot.StorageTotal > 0) Append(_storageHistory, Storage);
        OnPropertyChanged(nameof(Cores)); OnPropertyChanged(nameof(CpuTemperature)); OnPropertyChanged(nameof(MemoryLabel)); OnPropertyChanged(nameof(HasMemoryData)); OnPropertyChanged(nameof(MemoryCapacityLabel)); OnPropertyChanged(nameof(MemoryUsed)); OnPropertyChanged(nameof(MemoryDetail)); OnPropertyChanged(nameof(StorageUsed)); OnPropertyChanged(nameof(HasStorageData));
        OnPropertyChanged(nameof(NetworkSummary)); OnPropertyChanged(nameof(CpuHistory)); OnPropertyChanged(nameof(MemoryHistory)); OnPropertyChanged(nameof(StorageHistory));
    }

    private static void Append(List<double> history, double value) { history.Add(value); while (history.Count > HistoryLimit) history.RemoveAt(0); }
}
public sealed class CpuModuleViewModel : MonitorModuleViewModel
{
    private static readonly string[] WidgetKinds = ["Load", "Temperature", "Clock", "Cores", "History", "Power", "Voltage", "Temperatures", "PowerLimits", "Frequency", "CStates", "Information", "Processes", "PerCore", "Sensors"];
    private static readonly string[] DefaultWidgetKinds = ["Load", "Temperature", "Clock", "Cores", "History", "Power", "Voltage", "Temperatures", "PowerLimits", "Frequency", "Information", "Processes", "Sensors"];
    private static readonly IReadOnlyDictionary<string, (int Column, int Row, int ColumnSpan, int RowSpan)> PreviousDefaultGrid = new Dictionary<string, (int, int, int, int)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Load"] = (0, 0, 4, 2), ["Temperature"] = (4, 0, 4, 2), ["Clock"] = (8, 0, 4, 2),
        ["Cores"] = (0, 2, 6, 3), ["History"] = (6, 2, 6, 3),
        ["Power"] = (0, 5, 2, 2), ["Voltage"] = (2, 5, 2, 2), ["Temperatures"] = (4, 5, 2, 2),
        ["PowerLimits"] = (6, 5, 2, 2), ["Frequency"] = (8, 5, 4, 2),
        ["CStates"] = (0, 7, 3, 2), ["Information"] = (3, 7, 3, 2), ["Processes"] = (6, 7, 3, 2),
        ["PerCore"] = (9, 7, 3, 2), ["Sensors"] = (0, 9, 12, 2)
    };
    private const int HistoryLimit = 900;
    private double _usage, _temperature;
    private string _temperatureSource = "Sensor scan pending";
    private Color _accentColor = Color.Parse("#395B64");
    private string _selectedAccentPreset = "TEAL";
    private ICpuCustomizationRepository? _customizationRepository;
    private string _historyRange = "10m";
    private double _lastSurfaceWidth;
    private bool _isWidgetLibraryVisible;
    private bool _showHistoryLoad = true, _showHistoryTemperature = true, _showHistoryClock = true, _showHistoryPower = true;
    private bool _sensorsExpanded;

    public ObservableCollection<CoreLoadViewModel> CoreLoads { get; } = new();
    public ObservableCollection<CpuWidgetViewModel> Widgets { get; } = new();
    public ObservableCollection<CpuWidgetLibraryItemViewModel> WidgetLibrary { get; } = new();
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> TemperatureHistory { get; } = new();
    public ObservableCollection<string> HistoryRanges { get; } = new() { "1m", "5m", "10m", "30m" };
    public ObservableCollection<CpuMetricRowViewModel> SensorPreview { get; } = new();
    public ObservableCollection<string> AccentPresets { get; } = new() { "TEAL", "BLUE", "AMBER", "PURPLE" };
    public ObservableCollection<AccentColorViewModel> Accents { get; } = new();
    public CpuTelemetryViewModel Telemetry { get; } = new();
    public double Usage { get => _usage; private set => SetProperty(ref _usage, value); }
    public double Temperature { get => _temperature; private set => SetProperty(ref _temperature, value); }
    public string TemperatureSource { get => _temperatureSource; private set => SetProperty(ref _temperatureSource, value); }
    public Color AccentColor
    {
        get => _accentColor;
        set
        {
            if (!SetProperty(ref _accentColor, value)) return;
            OnPropertyChanged(nameof(AccentBrush));
            OnPropertyChanged(nameof(HistoryLoadBrush));
        }
    }
    public string SelectedAccentPreset
    {
        get => _selectedAccentPreset;
        set
        {
            if (!SetProperty(ref _selectedAccentPreset, value)) return;
            var accent = Accents.FirstOrDefault(item => item.Name == value);
            if (accent is not null)
            {
                AccentColor = accent.GetColor();
                OnPropertyChanged(nameof(SelectedAccentColor));
                OnPropertyChanged(nameof(SelectedAccentHex));
            }
        }
    }
    public Color SelectedAccentColor
    {
        get => Accents.FirstOrDefault(accent => accent.Name == SelectedAccentPreset)?.SelectedColor ?? AccentColor;
        set
        {
            var accent = Accents.FirstOrDefault(item => item.Name == SelectedAccentPreset);
            if (accent is null) return;
            accent.SelectedColor = value;
            AccentColor = value;
            OnPropertyChanged(nameof(SelectedAccentColor));
            OnPropertyChanged(nameof(SelectedAccentHex));
        }
    }
    public string SelectedAccentHex => $"#{SelectedAccentColor.R:X2}{SelectedAccentColor.G:X2}{SelectedAccentColor.B:X2}";
    public IBrush AccentBrush => new SolidColorBrush(AccentColor);
    public IBrush HistoryLoadBrush => new SolidColorBrush(SelectedAccentColor);
    public IBrush HistoryTemperatureBrush => GetAccentBrush("AMBER");
    public IBrush HistoryClockBrush => GetAccentBrush("BLUE");
    public IBrush HistoryPowerBrush => GetAccentBrush("PURPLE");
    public bool IsWidgetLibraryVisible { get => _isWidgetLibraryVisible; private set => SetProperty(ref _isWidgetLibraryVisible, value); }
    public string TemperatureLabel => Temperature <= 0 ? "Unavailable" : $"{Temperature:0}°C";
    public string Cores => $"{Environment.ProcessorCount} logical cores";
    public ICommand AddLoadCommand { get; }
    public ICommand AddTemperatureCommand { get; }
    public ICommand AddCoresCommand { get; }
    public ICommand RemoveWidgetCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand AddWidgetCommand { get; }
    public ICommand ResetLayoutCommand { get; }
    public ICommand ResetAccentsCommand { get; }
    public string HistoryRange { get => _historyRange; set { if (SetProperty(ref _historyRange, HistoryRanges.Contains(value) ? value : "10m")) { OnPropertyChanged(nameof(HistorySampleCount)); OnPropertyChanged(nameof(HistoryLoad)); OnPropertyChanged(nameof(HistoryTemperature)); OnPropertyChanged(nameof(HistoryClock)); OnPropertyChanged(nameof(HistoryPower)); OnPropertyChanged(nameof(HistoryTicks)); SaveCustomization(); } } }
    public int HistorySampleCount => HistoryRange switch { "1m" => 30, "5m" => 150, "30m" => 900, _ => 300 };
    public IReadOnlyList<string> HistoryTicks => HistoryRange switch
    {
        "1m" => ["-60s", "-48s", "-36s", "-24s", "-12s", "Now"],
        "5m" => ["-5m", "-4m", "-3m", "-2m", "-1m", "Now"],
        "30m" => ["-30m", "-24m", "-18m", "-12m", "-6m", "Now"],
        _ => ["-10m", "-8m", "-6m", "-4m", "-2m", "Now"]
    };
    public bool ShowHistoryLoad { get => _showHistoryLoad; set { if (SetProperty(ref _showHistoryLoad, value)) SaveCustomization(); } }
    public bool ShowHistoryTemperature { get => _showHistoryTemperature; set { if (SetProperty(ref _showHistoryTemperature, value)) SaveCustomization(); } }
    public bool ShowHistoryClock { get => _showHistoryClock; set { if (SetProperty(ref _showHistoryClock, value)) SaveCustomization(); } }
    public bool ShowHistoryPower { get => _showHistoryPower; set { if (SetProperty(ref _showHistoryPower, value)) SaveCustomization(); } }
    public bool SensorsExpanded { get => _sensorsExpanded; set { if (SetProperty(ref _sensorsExpanded, value)) { OnPropertyChanged(nameof(SensorsCollapsed)); OnPropertyChanged(nameof(SensorsToggleText)); SaveCustomization(); } } }
    public bool SensorsCollapsed => !SensorsExpanded;
    public IReadOnlyList<double> HistoryLoad => CpuHistory.TakeLast(HistorySampleCount).ToArray();
    public IReadOnlyList<double> HistoryTemperature => TemperatureHistory.TakeLast(HistorySampleCount).ToArray();
    public IReadOnlyList<double> HistoryClock => Telemetry.ClockHistory.TakeLast(HistorySampleCount).ToArray();
    public IReadOnlyList<double> HistoryPower => Telemetry.PowerHistory.TakeLast(HistorySampleCount).ToArray();
    public int HiddenSensorCount => Math.Max(0, Telemetry.AllSensors.Count - SensorPreview.Count);
    public string SensorsHeader => $"ALL EXPOSED CPU SENSORS ({Telemetry.AllSensors.Count})";
    public string SensorsToggleText => SensorsExpanded ? "SHOW LESS" : $"SHOW ALL  +{HiddenSensorCount}";
    public bool CanAddLoad => Widgets.All(widget => !widget.IsLoad);
    public bool CanAddTemperature => Widgets.All(widget => !widget.IsTemperature);
    public bool CanAddCores => Widgets.All(widget => !widget.IsCores);

    public CpuModuleViewModel() : base("CPU")
    {
        Accents.Add(new AccentColorViewModel("TEAL", "#395B64", AccentChanged));
        Accents.Add(new AccentColorViewModel("BLUE", "#3264A5", AccentChanged));
        Accents.Add(new AccentColorViewModel("AMBER", "#B66A1C", AccentChanged));
        Accents.Add(new AccentColorViewModel("PURPLE", "#7650A8", AccentChanged));
        AddLoadCommand = new RelayCommand(_ => AddWidget("Load"));
        AddTemperatureCommand = new RelayCommand(_ => AddWidget("Temperature"));
        AddCoresCommand = new RelayCommand(_ => AddWidget("Cores"));
        RemoveWidgetCommand = new RelayCommand(RemoveWidget);
        ResetCommand = new RelayCommand(_ => ResetCustomization());
        AddWidgetCommand = new RelayCommand(parameter => AddWidget(parameter?.ToString() ?? string.Empty));
        ResetLayoutCommand = new RelayCommand(_ => ResetLayout());
        ResetAccentsCommand = new RelayCommand(_ => ResetAccents());
        foreach (var kind in WidgetKinds) WidgetLibrary.Add(new CpuWidgetLibraryItemViewModel(kind, () => AddWidget(kind)));
        foreach (var kind in DefaultWidgetKinds)
            AddWidget(kind, save: false);
    }

    public void SetWidgetLibraryVisible(bool visible) => IsWidgetLibraryVisible = visible;

    public void LoadCustomization(ICpuCustomizationRepository repository)
    {
        _customizationRepository = repository;
        var settings = repository.LoadCpuCustomization();
        foreach (var accent in Accents)
            if (settings.AccentColors.TryGetValue(accent.Name, out var hex)) accent.Load(hex);
        _showHistoryLoad = settings.HistorySeries.Contains("Load", StringComparison.OrdinalIgnoreCase);
        _showHistoryTemperature = settings.HistorySeries.Contains("Temp", StringComparison.OrdinalIgnoreCase);
        _showHistoryClock = settings.HistorySeries.Contains("Clock", StringComparison.OrdinalIgnoreCase);
        _showHistoryPower = settings.HistorySeries.Contains("Power", StringComparison.OrdinalIgnoreCase);
        _historyRange = HistoryRanges.Contains(settings.HistoryRange) ? settings.HistoryRange : "10m";
        _sensorsExpanded = settings.SensorsExpanded;
        if (IsPreviousDefaultLayout(settings.Widgets))
        {
            var previous = settings.Widgets.ToDictionary(widget => widget.Kind, StringComparer.OrdinalIgnoreCase);
            Widgets.Clear();
            foreach (var kind in DefaultWidgetKinds.Where(previous.ContainsKey))
            {
                AddWidget(kind, save: false);
                if (!previous.TryGetValue(kind, out var old)) continue;
                var restored = Widgets.Last();
                restored.LoadStyles(settings.WidgetStyles.Where(style => style.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)));
                restored.Style = old.Style;
                try { restored.SetAccent(Color.Parse(old.AccentColorHex)); } catch (FormatException) { }
            }
        }
        else if (settings.Widgets.Count > 0)
        {
            var oldPixelLayout = settings.Widgets.Any(widget => widget.X > 12 || widget.Y > 40 || widget.Width > 12 || widget.Height > 8);
            Widgets.Clear();
            if (oldPixelLayout)
            {
                foreach (var kind in settings.Widgets.Select(widget => widget.Kind).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (WidgetKinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) AddWidget(kind, save: false);
                foreach (var previous in settings.Widgets)
                {
                    var loaded = Widgets.FirstOrDefault(item => item.Kind.Equals(previous.Kind, StringComparison.OrdinalIgnoreCase));
                    if (loaded is null) continue;
                    loaded.LoadStyles(settings.WidgetStyles.Where(style => style.Kind.Equals(previous.Kind, StringComparison.OrdinalIgnoreCase)));
                    loaded.Style = previous.Style;
                    try { loaded.SetAccent(Color.Parse(previous.AccentColorHex)); } catch (FormatException) { }
                }
            }
            else foreach (var widget in settings.Widgets)
            {
                if (!WidgetKinds.Contains(widget.Kind, StringComparer.OrdinalIgnoreCase)) continue;
                AddWidget(widget.Kind, widget.X, widget.Y, widget.Width, widget.Height, widget.Style, widget.AccentColorHex, save: false);
                var loadedWidget = Widgets.LastOrDefault();
                if (loadedWidget is not null)
                {
                    loadedWidget.LoadStyles(settings.WidgetStyles.Where(style => style.Kind.Equals(widget.Kind, StringComparison.OrdinalIgnoreCase)));
                    loadedWidget.Style = widget.Style;
                }
            }
            NotifyAddAvailability();
        }
        OnPropertyChanged(nameof(HistoryRange)); OnPropertyChanged(nameof(HistorySampleCount)); OnPropertyChanged(nameof(HistoryTicks));
        OnPropertyChanged(nameof(ShowHistoryLoad)); OnPropertyChanged(nameof(ShowHistoryTemperature)); OnPropertyChanged(nameof(ShowHistoryClock)); OnPropertyChanged(nameof(ShowHistoryPower));
        OnPropertyChanged(nameof(SensorsExpanded)); OnPropertyChanged(nameof(SensorsCollapsed));
        SelectedAccentPreset = Accents.Any(accent => accent.Name == _selectedAccentPreset) ? _selectedAccentPreset : "TEAL";
        OnPropertyChanged(nameof(HistoryTemperatureBrush)); OnPropertyChanged(nameof(HistoryClockBrush)); OnPropertyChanged(nameof(HistoryPowerBrush));
        SaveCustomization();
    }

    public void SaveCustomization()
    {
        _customizationRepository?.SaveCpuCustomization(new CpuCustomizationSettings(
            Widgets.Select(widget => new CpuWidgetLayout(widget.Kind, widget.GridColumn, widget.GridRow, widget.GridColumnSpan, widget.GridRowSpan, widget.Style, widget.AccentColorHex)).ToList(),
            Accents.ToDictionary(accent => accent.Name, accent => accent.HexText, StringComparer.OrdinalIgnoreCase),
            Widgets.SelectMany(widget => widget.Styles.Select(style => new CpuWidgetStyleSettings(widget.Kind, style.Index, style.BackgroundHex, style.TextHex, style.MutedHex, style.BorderHex, style.AccentHex))).ToList(),
            string.Join(',', new[] { ShowHistoryLoad ? "Load" : null, ShowHistoryTemperature ? "Temp" : null, ShowHistoryClock ? "Clock" : null, ShowHistoryPower ? "Power" : null }.Where(value => value is not null)),
            HistoryRange,
            SensorsExpanded));
    }

    public void ResetCustomization()
    {
        ResetAccents();
        ResetLayout();
    }

    public void ResetAccents()
    {
        foreach (var accent in Accents) accent.Reset(accent.Name switch { "BLUE" => "#3264A5", "AMBER" => "#B66A1C", "PURPLE" => "#7650A8", _ => "#395B64" });
        _selectedAccentPreset = "TEAL";
        OnPropertyChanged(nameof(SelectedAccentPreset));
        AccentColor = Accents[0].GetColor();
        OnPropertyChanged(nameof(HistoryTemperatureBrush)); OnPropertyChanged(nameof(HistoryClockBrush)); OnPropertyChanged(nameof(HistoryPowerBrush));
        SaveCustomization();
    }

    public override void Update(SystemSnapshot s)
    {
        Usage = s.CpuUsage;
        Temperature = s.CpuTemperature;
        TemperatureSource = s.TemperatureSource;
        Telemetry.Update(s);
        Append(CpuHistory, s.CpuUsage);
        if (s.CpuTemperature > 0) Append(TemperatureHistory, s.CpuTemperature);
        CoreLoads.Clear();
        foreach (var core in s.Cores) CoreLoads.Add(new CoreLoadViewModel(core));
        var cpuValues = CpuHistory.ToArray();
        var temperatureValues = TemperatureHistory.ToArray();
        foreach (var widget in Widgets) widget.Update(s, cpuValues, temperatureValues, Telemetry);
        ObservableCollectionReconciler.SetItems(SensorPreview, Telemetry.AllSensors.Take(6).ToArray());
        OnPropertyChanged(nameof(HiddenSensorCount)); OnPropertyChanged(nameof(SensorsHeader)); OnPropertyChanged(nameof(SensorsToggleText));
        OnPropertyChanged(nameof(HistoryLoad)); OnPropertyChanged(nameof(HistoryTemperature)); OnPropertyChanged(nameof(HistoryClock)); OnPropertyChanged(nameof(HistoryPower));
        foreach (var item in WidgetLibrary) item.Refresh(Widgets.Any(widget => widget.Kind == item.Kind));
        OnPropertyChanged(nameof(TemperatureLabel));
    }

    private void AddWidget(string kind, double? x = null, double? y = null, double? width = null, double? height = null, int style = 1, string? accentColorHex = null, bool save = true)
    {
        if (!WidgetKinds.Contains(kind, StringComparer.OrdinalIgnoreCase) || Widgets.Any(widget => widget.Kind == kind)) return;
        var placement = DefaultGrid(kind);
        var widget = new CpuWidgetViewModel(kind, 0, 0, 260, 200)
        {
            GridColumn = (int)(x ?? placement.Column), GridRow = (int)(y ?? placement.Row),
            GridColumnSpan = (int)(width ?? placement.ColumnSpan), GridRowSpan = (int)(height ?? placement.RowSpan)
        };
        widget.Style = style;
        widget.SetAccent(AccentColor);
        if (!string.IsNullOrWhiteSpace(accentColorHex))
        {
            try { widget.SetAccent(Color.Parse(accentColorHex)); } catch (FormatException) { }
        }
        widget.Changed = () => { if (_lastSurfaceWidth > 0) ResizeWidgets(_lastSurfaceWidth); SaveCustomization(); };
        if (save && x is null && y is null) PlaceInNextAvailableGridSlot(widget);
        Widgets.Add(widget);
        NotifyAddAvailability();
        foreach (var item in WidgetLibrary) item.Refresh(Widgets.Any(existing => existing.Kind == item.Kind));
        if (save && _lastSurfaceWidth > 0) ResizeWidgets(_lastSurfaceWidth);
        if (save) SaveCustomization();
    }

    private void RemoveWidget(object? value)
    {
        if (value is CpuWidgetViewModel widget && Widgets.Remove(widget))
        {
            NotifyAddAvailability();
            foreach (var item in WidgetLibrary) item.Refresh(Widgets.Any(existing => existing.Kind == item.Kind));
            if (_lastSurfaceWidth > 0) ResizeWidgets(_lastSurfaceWidth);
            SaveCustomization();
        }
    }

    public void ResizeWidgets(double surfaceWidth, double surfaceHeight = 0)
    {
        if (surfaceWidth <= 0) return;
        _lastSurfaceWidth = surfaceWidth;
        const double margin = 12, gap = 12, rowUnit = 110;
        var usableWidth = Math.Max(160, surfaceWidth - margin * 2);
        var columns = usableWidth < 480 ? 2 : usableWidth < 900 ? 6 : 12;
        var cellWidth = Math.Max(1, (usableWidth - gap * (columns - 1)) / columns);
        var occupied = new HashSet<(int Column, int Row)>();
        var placements = new List<(CpuWidgetViewModel Widget, int Column, int Row, int Span, int Rows)>();
        var ordered = Widgets.OrderBy(widget => widget.GridRow).ThenBy(widget => widget.GridColumn).ThenBy(widget => Array.IndexOf(WidgetKinds, widget.Kind)).ToArray();
        foreach (var widget in ordered)
        {
            var desired = ResponsivePlacement(widget, columns);
            var span = Math.Clamp(desired.Span, 1, columns);
            var rows = widget.IsCollapsed ? 1 : widget.IsSensors && SensorsExpanded ? Math.Max(4, desired.Rows) : desired.Rows;
            var column = Math.Clamp(desired.Column, 0, columns - span);
            var row = Math.Max(0, desired.Row);
            if (columns != 12)
            {
                column = 0;
                while (!CanPlace(occupied, column, row, span, rows, columns))
                {
                    column++;
                    if (column + span > columns) { column = 0; row++; }
                }
            }
            else
            {
                while (!CanPlace(occupied, column, row, span, rows, columns)) row++;
            }
            Mark(occupied, column, row, span, rows);
            placements.Add((widget, column, row, span, rows));
        }
        var bottom = 0d;
        foreach (var item in placements)
        {
            var cardWidth = item.Span * cellWidth + (item.Span - 1) * gap;
            var cardHeight = item.Rows * rowUnit + (item.Rows - 1) * gap;
            item.Widget.X = margin + item.Column * (cellWidth + gap);
            item.Widget.Y = margin + item.Row * (rowUnit + gap);
            item.Widget.Width = Math.Max(CpuWidgetViewModel.MinimumWidth, cardWidth);
            item.Widget.Height = Math.Max(CpuWidgetViewModel.MinimumHeight, cardHeight);
            bottom = Math.Max(bottom, item.Widget.Y + item.Widget.Height);
        }
        DashboardHeight = bottom + margin;
        OnPropertyChanged(nameof(DashboardHeight));
    }

    public double DashboardHeight { get; private set; } = 500;

    public void CaptureWidgetGridLayout(double surfaceWidth)
    {
        if (surfaceWidth <= 0) return;
        const double margin = 12, gap = 12, rowUnit = 110;
        var columns = surfaceWidth - margin * 2 < 480 ? 2 : surfaceWidth - margin * 2 < 900 ? 6 : 12;
        var cellWidth = Math.Max(1, (surfaceWidth - margin * 2 - gap * (columns - 1)) / columns);
        foreach (var widget in Widgets)
        {
            widget.GridColumn = Math.Clamp((int)Math.Round((widget.X - margin) / (cellWidth + gap) * 12d / columns), 0, 11);
            widget.GridRow = Math.Max(0, (int)Math.Round((widget.Y - margin) / (rowUnit + gap)));
            widget.GridColumnSpan = Math.Clamp((int)Math.Round(widget.Width / (cellWidth + gap) * 12d / columns), 1, 12);
            widget.GridRowSpan = Math.Clamp((int)Math.Round(widget.Height / (rowUnit + gap)), 1, 8);
        }
        SaveCustomization();
    }

    private static (int Column, int Row, int ColumnSpan, int RowSpan) DefaultGrid(string kind) => kind switch
    {
        "Load" => (0, 0, 4, 2), "Temperature" => (4, 0, 4, 2), "Clock" => (8, 0, 4, 2),
        "Cores" => (0, 2, 6, 2), "History" => (6, 2, 6, 2),
        "Power" => (0, 4, 2, 1), "Voltage" => (2, 4, 2, 1), "Temperatures" => (4, 4, 2, 1),
        "PowerLimits" => (6, 4, 2, 1), "Frequency" => (8, 4, 4, 1),
        "Information" => (0, 5, 4, 1), "Processes" => (4, 5, 4, 1), "Sensors" => (8, 5, 4, 1),
        "CStates" => (0, 6, 3, 2), "PerCore" => (0, 6, 6, 3), _ => (0, 7, 4, 2)
    };

    private (int Column, int Row, int Span, int Rows) ResponsivePlacement(CpuWidgetViewModel widget, int columns)
    {
        if (columns == 12) return (widget.GridColumn, widget.GridRow, widget.GridColumnSpan, widget.GridRowSpan);
        var rows = widget.IsCollapsed ? 1 : widget.Kind switch { "Cores" or "History" => 2, "PerCore" => 3, "Sensors" => SensorsExpanded ? 4 : 1, _ => widget.GridRowSpan };
        if (columns == 6)
        {
            var span = widget.Kind is "Cores" or "History" or "PerCore" or "Sensors" or "Frequency" ? 6 : 3;
            return (0, 0, span, rows);
        }
        return (0, 0, 2, widget.IsCores && !widget.IsCollapsed ? 6 : rows);
    }

    private static bool CanPlace(HashSet<(int Column, int Row)> occupied, int column, int row, int span, int rows, int maxColumns)
    {
        if (column < 0 || column + span > maxColumns) return false;
        for (var y = row; y < row + rows; y++) for (var x = column; x < column + span; x++) if (occupied.Contains((x, y))) return false;
        return true;
    }

    private static void Mark(HashSet<(int Column, int Row)> occupied, int column, int row, int span, int rows)
    {
        for (var y = row; y < row + rows; y++) for (var x = column; x < column + span; x++) occupied.Add((x, y));
    }

    private void PlaceInNextAvailableGridSlot(CpuWidgetViewModel widget)
    {
        var placement = DefaultGrid(widget.Kind);
        widget.GridColumn = 0;
        widget.GridRow = Widgets.Select(item => item.GridRow + item.GridRowSpan).DefaultIfEmpty(0).Max();
        widget.GridColumnSpan = placement.ColumnSpan;
        widget.GridRowSpan = placement.RowSpan;
    }

    private void ResetLayout()
    {
        var oldWidgets = Widgets.ToDictionary(widget => widget.Kind, StringComparer.OrdinalIgnoreCase);
        Widgets.Clear();
        foreach (var kind in DefaultWidgetKinds)
        {
            AddWidget(kind, save: false);
            if (!oldWidgets.TryGetValue(kind, out var old)) continue;
            var restored = Widgets.Last();
            restored.Style = old.Style;
            restored.SetAccent(old.SelectedColor);
            restored.LoadStyles(old.Styles.Select(style => new CpuWidgetStyleSettings(kind, style.Index, style.BackgroundHex, style.TextHex, style.MutedHex, style.BorderHex, style.AccentHex)));
        }
        ShowHistoryLoad = ShowHistoryTemperature = ShowHistoryClock = ShowHistoryPower = true;
        HistoryRange = "10m";
        SensorsExpanded = false;
        NotifyAddAvailability();
        foreach (var item in WidgetLibrary) item.Refresh(Widgets.Any(widget => widget.Kind == item.Kind));
        if (_lastSurfaceWidth > 0) ResizeWidgets(_lastSurfaceWidth);
        SaveCustomization();
    }

    private void NotifyAddAvailability()
    {
        OnPropertyChanged(nameof(CanAddLoad));
        OnPropertyChanged(nameof(CanAddTemperature));
        OnPropertyChanged(nameof(CanAddCores));
    }

    private void AccentChanged(AccentColorViewModel accent)
    {
        if (accent.Name == SelectedAccentPreset)
        {
            AccentColor = accent.GetColor();
            OnPropertyChanged(nameof(SelectedAccentColor));
            OnPropertyChanged(nameof(SelectedAccentHex));
        }
        OnPropertyChanged(nameof(HistoryTemperatureBrush));
        OnPropertyChanged(nameof(HistoryClockBrush));
        OnPropertyChanged(nameof(HistoryPowerBrush));
        SaveCustomization();
    }

    private IBrush GetAccentBrush(string name) => Accents.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.SwatchBrush ?? AccentBrush;

    private static bool IsPreviousDefaultLayout(IReadOnlyList<CpuWidgetLayout> widgets)
    {
        if (widgets.Count < 10 || !widgets.Any(widget => widget.Kind.Equals("Cores", StringComparison.OrdinalIgnoreCase) && widget.X == 0 && widget.Y == 2 && widget.Width == 6 && widget.Height == 3)) return false;
        return widgets.All(widget => PreviousDefaultGrid.TryGetValue(widget.Kind, out var position) &&
            widget.X == position.Column && widget.Y == position.Row && widget.Width == position.ColumnSpan && widget.Height == position.RowSpan);
    }

    private static void Append(ObservableCollection<double> values, double value)
    {
        values.Add(value);
        while (values.Count > HistoryLimit) values.RemoveAt(0);
    }
}

public sealed class CpuWidgetLibraryItemViewModel : ViewModelBase
{
    private readonly Action _add;
    private bool _isAdded;
    public string Kind { get; }
    public string Title => Kind switch
    {
        "Load" => "CPU utilization", "Temperature" => "CPU temperature", "Clock" => "CPU clock", "Cores" => "Logical cores",
        "History" => "History chart", "Power" => "CPU power", "Voltage" => "Voltage", "Temperatures" => "Temperature sensors",
        "PowerLimits" => "Power limits", "Frequency" => "Frequency distribution", "CStates" => "C-state residency", "Information" => "CPU information",
        "Processes" => "Top CPU processes", "PerCore" => "Per-core statistics", "Sensors" => "All exposed sensors", _ => Kind
    };
    public string Summary => Kind switch
    {
        "Load" => "Utilization", "Temperature" => "Package / cores", "Clock" => "Frequency", "Cores" => "Per-thread load",
        "History" => "Live metric chart", "Power" => "Package / core / SoC", "Voltage" => "CPU rail", "Temperatures" => "Package / CCD / core",
        "PowerLimits" => "PPT / TDC / EDC", "Frequency" => "Clock ranges", "CStates" => "Idle residency", "Information" => "Processor details",
        "Processes" => "Highest CPU use", "PerCore" => "Load, clock, temperature", "Sensors" => "Hardware channels", _ => string.Empty
    };
    public bool IsAdded { get => _isAdded; private set { if (SetProperty(ref _isAdded, value)) { OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(CanAdd)); } } }
    public bool CanAdd => !IsAdded;
    public string ActionText => IsAdded ? "ADDED" : "ADD";
    public ICommand AddCommand { get; }
    public CpuWidgetLibraryItemViewModel(string kind, Action add) { Kind = kind; _add = add; AddCommand = new RelayCommand(_ => Add()); }
    public void Refresh(bool added) => IsAdded = added;
    public void Add() { if (!IsAdded) _add(); }
}

public sealed class CoreLoadViewModel : ViewModelBase
{
    private double _usagePercent;
    private Avalonia.Media.IBrush? _accentBrush, _borderBrush, _textBrush;
    public int Index { get; }
    public string Label => $"Core {Index:00}";
    public double UsagePercent { get => _usagePercent; private set { _usagePercent = value; OnPropertyChanged(nameof(UsagePercent)); OnPropertyChanged(nameof(FillWidth)); } }
    public double FillWidth => Math.Round(Math.Clamp(UsagePercent, 0, 100) * 122d / 100d, 1);
    public Avalonia.Media.IBrush? AccentBrush { get => _accentBrush; private set { _accentBrush = value; OnPropertyChanged(nameof(AccentBrush)); } }
    public Avalonia.Media.IBrush? BorderBrush { get => _borderBrush; private set { _borderBrush = value; OnPropertyChanged(nameof(BorderBrush)); } }
    public Avalonia.Media.IBrush? TextBrush { get => _textBrush; private set { _textBrush = value; OnPropertyChanged(nameof(TextBrush)); } }
    public IReadOnlyList<CoreLoadSegmentViewModel> Segments { get; private set; } = Array.Empty<CoreLoadSegmentViewModel>();

    public CoreLoadViewModel(CoreMetric metric, Avalonia.Media.IBrush? accentBrush = null, Avalonia.Media.IBrush? borderBrush = null, Avalonia.Media.IBrush? textBrush = null)
    {
        Index = metric.Index;
        Update(metric, accentBrush, borderBrush, textBrush);
    }

    public void Update(CoreMetric metric, Avalonia.Media.IBrush? accentBrush, Avalonia.Media.IBrush? borderBrush, Avalonia.Media.IBrush? textBrush)
    {
        UsagePercent = metric.UsagePercent;
        UpdateBrushes(accentBrush, borderBrush, textBrush);
    }

    public void UpdateBrushes(Avalonia.Media.IBrush? accentBrush, Avalonia.Media.IBrush? borderBrush, Avalonia.Media.IBrush? textBrush)
    {
        AccentBrush = accentBrush; BorderBrush = borderBrush; TextBrush = textBrush;
        var filledSegments = (int)Math.Ceiling(Math.Clamp(UsagePercent, 0, 100) / 20d);
        Segments = Enumerable.Range(0, 5).Select(index => new CoreLoadSegmentViewModel(index < filledSegments, accentBrush, borderBrush)).ToArray();
        OnPropertyChanged(nameof(Segments));
    }
}

public sealed class CoreLoadSegmentViewModel
{
    public Avalonia.Media.IBrush? Brush { get; }

    public CoreLoadSegmentViewModel(bool isFilled, Avalonia.Media.IBrush? accentBrush, Avalonia.Media.IBrush? trackBrush)
    {
        Brush = isFilled ? accentBrush : trackBrush;
    }
}
public sealed class MemoryModuleViewModel : MonitorModuleViewModel
{
    private const int HistoryLimit = 30;
    private readonly List<double> _history = new();
    private double _usage, _totalGigabytes;
    public ObservableCollection<ProcessSampleRowViewModel> TopConsumers { get; } = new();
    public bool HasConsumers => TopConsumers.Count > 0;
    public bool HasNoConsumers => !HasConsumers;
    public double Usage { get => _usage; private set => SetProperty(ref _usage, value); }
    public double TotalGigabytes { get => _totalGigabytes; private set => SetProperty(ref _totalGigabytes, value); }
    public bool HasData => TotalGigabytes > 0;
    public string Used => HasData ? $"{TotalGigabytes * Usage / 100:0.0} GB" : "N/A";
    public string Available => HasData ? $"{Math.Max(0, TotalGigabytes - TotalGigabytes * Usage / 100):0.0} GB" : "N/A";
    public string Total => HasData ? $"{TotalGigabytes:0.0} GB" : "N/A";
    public string UtilizationLabel => HasData ? $"{Usage:0.0}%" : "N/A";
    public string UsageBand => !HasData ? "WAITING FOR MEMORY SAMPLE" : Usage >= 90 ? "VERY HIGH UTILIZATION" : Usage >= 75 ? "HIGH UTILIZATION" : Usage >= 40 ? "MODERATE UTILIZATION" : "LOW UTILIZATION";
    public IReadOnlyList<double> History => _history.ToArray();
    public MemoryModuleViewModel() : base("Memory") { }

    public override void Update(SystemSnapshot snapshot)
    {
        Usage = snapshot.MemoryUsage; TotalGigabytes = snapshot.MemoryTotal;
        if (HasData) { _history.Add(Usage); while (_history.Count > HistoryLimit) _history.RemoveAt(0); }
        var consumers = ProcessSampleRowViewModel.Reconcile(snapshot.Processes.OrderByDescending(item => item.Memory).Take(5), TopConsumers);
        ObservableCollectionReconciler.SetItems(TopConsumers, consumers);
        OnPropertyChanged(nameof(HasData)); OnPropertyChanged(nameof(HasConsumers)); OnPropertyChanged(nameof(HasNoConsumers)); OnPropertyChanged(nameof(Used)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(UtilizationLabel)); OnPropertyChanged(nameof(UsageBand)); OnPropertyChanged(nameof(History));
    }
}

public sealed class StorageModuleViewModel : MonitorModuleViewModel
{
    private double _usage, _totalGigabytes;
    private string _primaryName = "No ready volume";
    public ObservableCollection<StorageVolumeViewModel> Disks { get; } = new();
    public double Usage { get => _usage; private set => SetProperty(ref _usage, value); }
    public double TotalGigabytes { get => _totalGigabytes; private set => SetProperty(ref _totalGigabytes, value); }
    public string PrimaryName { get => _primaryName; private set => SetProperty(ref _primaryName, value); }
    public bool HasData => TotalGigabytes > 0;
    public int DiskCount => Disks.Count;
    public bool HasDisks => DiskCount > 0;
    public bool HasNoDisks => DiskCount == 0;
    public double TotalCapacityGigabytes => Disks.Sum(disk => disk.TotalGigabytes);
    public double TotalUsedGigabytes => Disks.Sum(disk => disk.UsedGigabytes);
    public string Used => HasData ? $"{TotalGigabytes * Usage / 100:0.0} GB" : "N/A";
    public string Total => HasData ? $"{TotalGigabytes:0.0} GB total" : "N/A";
    public string CapacitySummary => DiskCount == 0 ? "No ready local volumes" : $"{DiskCount} ready local volume{(DiskCount == 1 ? "" : "s")} · sampled by Windows";
    public string AllVolumesSummary => DiskCount == 0 ? "N/A" : $"{TotalUsedGigabytes:0.0} / {TotalCapacityGigabytes:0.0} GB";

    public StorageModuleViewModel() : base("Storage") { }

    public override void Update(SystemSnapshot snapshot)
    {
        var primary = snapshot.Disks.FirstOrDefault();
        Usage = primary?.UsagePercent ?? 0; TotalGigabytes = primary?.TotalGigabytes ?? 0;
        PrimaryName = primary is null ? "No ready volume" : $"{primary.Name} · {primary.VolumeLabel}";
        var currentVolumes = Disks.ToDictionary(disk => disk.Name, StringComparer.OrdinalIgnoreCase);
        var volumes = new List<StorageVolumeViewModel>();
        foreach (var disk in snapshot.Disks)
        {
            if (currentVolumes.TryGetValue(disk.Name, out var volume)) volume.Update(disk);
            else volume = new StorageVolumeViewModel(disk);
            volumes.Add(volume);
        }
        ObservableCollectionReconciler.SetItems(Disks, volumes);
        OnPropertyChanged(nameof(HasData)); OnPropertyChanged(nameof(DiskCount)); OnPropertyChanged(nameof(HasDisks)); OnPropertyChanged(nameof(HasNoDisks)); OnPropertyChanged(nameof(TotalCapacityGigabytes));
        OnPropertyChanged(nameof(TotalUsedGigabytes)); OnPropertyChanged(nameof(Used)); OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(CapacitySummary)); OnPropertyChanged(nameof(AllVolumesSummary));
    }
}
public sealed class NetworkModuleViewModel : MonitorModuleViewModel
{
    private const int HistoryLimit = 60;
    private readonly List<double> _downloadHistory = new(), _uploadHistory = new();
    private double _download, _upload;
    private int _snapshotCount;
    public double Download { get => _download; private set => SetProperty(ref _download, value); }
    public double Upload { get => _upload; private set => SetProperty(ref _upload, value); }
    public IReadOnlyList<double> DownloadHistory => _downloadHistory.ToArray();
    public IReadOnlyList<double> UploadHistory => _uploadHistory.ToArray();
    public double ScaleMaximum => Math.Max(10, Math.Ceiling(Math.Max(_downloadHistory.DefaultIfEmpty().Max(), _uploadHistory.DefaultIfEmpty().Max()) / 10d) * 10d);
    public double PeakDownload => _downloadHistory.DefaultIfEmpty().Max();
    public double PeakUpload => _uploadHistory.DefaultIfEmpty().Max();
    public double CombinedThroughput => Download + Upload;
    public int SampleCount => _downloadHistory.Count;
    public bool HasSamples => SampleCount > 0;
    public bool HasNoSamples => !HasSamples;
    public string DownloadLabel => HasSamples ? $"{Download:0.00} Mbps" : "N/A";
    public string UploadLabel => HasSamples ? $"{Upload:0.00} Mbps" : "N/A";
    public string PeakDownloadLabel => HasSamples ? $"{PeakDownload:0.00} Mbps" : "N/A";
    public string PeakUploadLabel => HasSamples ? $"{PeakUpload:0.00} Mbps" : "N/A";
    public string CombinedLabel => HasSamples ? $"{CombinedThroughput:0.00} Mbps" : "Collecting…";
    public string AggregateLabel => HasSamples ? $"↓ {Download:0.00} Mbps   ↑ {Upload:0.00} Mbps" : "Collecting network counter baseline…";
    public string SamplingNote => HasSamples ? "Aggregate Windows interface counters · up to 60 calculated intervals" : "Waiting for a second counter sample to calculate throughput";
    public NetworkModuleViewModel() : base("Network") { }
    public override void Update(SystemSnapshot snapshot)
    {
        Download = snapshot.DownloadMbps; Upload = snapshot.UploadMbps;
        if (_snapshotCount > 0) { Append(_downloadHistory, Download); Append(_uploadHistory, Upload); }
        _snapshotCount++;
        OnPropertyChanged(nameof(DownloadHistory)); OnPropertyChanged(nameof(UploadHistory)); OnPropertyChanged(nameof(ScaleMaximum));
        OnPropertyChanged(nameof(PeakDownload)); OnPropertyChanged(nameof(PeakUpload)); OnPropertyChanged(nameof(AggregateLabel));
        OnPropertyChanged(nameof(CombinedThroughput)); OnPropertyChanged(nameof(SampleCount)); OnPropertyChanged(nameof(HasSamples)); OnPropertyChanged(nameof(HasNoSamples));
        OnPropertyChanged(nameof(DownloadLabel)); OnPropertyChanged(nameof(UploadLabel)); OnPropertyChanged(nameof(PeakDownloadLabel)); OnPropertyChanged(nameof(PeakUploadLabel)); OnPropertyChanged(nameof(CombinedLabel)); OnPropertyChanged(nameof(SamplingNote));
    }
    private static void Append(List<double> values, double value) { if (!double.IsFinite(value) || value < 0) return; values.Add(value); while (values.Count > HistoryLimit) values.RemoveAt(0); }
}

public sealed class ProcessModuleViewModel : MonitorModuleViewModel
{
    private IReadOnlyList<ProcessSampleRowViewModel> _sampled = Array.Empty<ProcessSampleRowViewModel>();
    private string _searchText = "", _sortMode = "CPU";
    public ObservableCollection<ProcessSampleRowViewModel> Items { get; } = new();
    public ObservableCollection<string> SortModes { get; } = new() { "CPU", "Memory", "Name" };
    public int Count { get; private set; }
    public int VisibleCount => Items.Count;
    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => Items.Count == 0;
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RefreshRows(); } }
    public string SortMode { get => _sortMode; set { if (SetProperty(ref _sortMode, value)) RefreshRows(); } }
    public string CountSummary => $"Showing {VisibleCount} sampled rows · {Count} total processes";
    public string CpuSummary => Items.Count == 0 ? "N/A" : $"{Items.Sum(item => item.Cpu):0.0}% CPU across shown rows";
    public string MemorySummary => Items.Count == 0 ? "N/A" : $"{Items.Sum(item => item.Memory):0} MB across shown rows";
    public ProcessModuleViewModel() : base("Processes") { }
    public override void Update(SystemSnapshot snapshot)
    {
        _sampled = ProcessSampleRowViewModel.Reconcile(snapshot.Processes, _sampled); Count = snapshot.ProcessCount;
        OnPropertyChanged(nameof(Count)); RefreshRows();
    }
    private void RefreshRows()
    {
        var filtered = _sampled.Where(item => string.IsNullOrWhiteSpace(SearchText) || item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || item.User.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        filtered = SortMode switch { "Memory" => filtered.OrderByDescending(item => item.Memory), "Name" => filtered.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase), _ => filtered.OrderByDescending(item => item.Cpu) };
        ObservableCollectionReconciler.SetItems(Items, filtered.ToArray());
        OnPropertyChanged(nameof(VisibleCount)); OnPropertyChanged(nameof(HasItems)); OnPropertyChanged(nameof(HasNoItems)); OnPropertyChanged(nameof(CountSummary)); OnPropertyChanged(nameof(CpuSummary)); OnPropertyChanged(nameof(MemorySummary));
    }
}
public sealed class OverlayModuleViewModel : MonitorModuleViewModel
{
    private bool _isEnabled, _alwaysOnTop = true, _showCpu = true, _showMemory = true, _showNetwork = true;
    private double _cpu, _memory, _memoryTotal, _download, _upload;
    private int _networkSamples;
    private Action<bool>? _visibilityChanged, _topmostChanged;
    public bool IsEnabled { get => _isEnabled; set { if (SetProperty(ref _isEnabled, value)) { OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(PreviewOpacity)); _visibilityChanged?.Invoke(value); } } }
    public bool AlwaysOnTop { get => _alwaysOnTop; set { if (SetProperty(ref _alwaysOnTop, value)) _topmostChanged?.Invoke(value); } }
    public bool ShowCpu { get => _showCpu; set { if (SetProperty(ref _showCpu, value)) NotifyVisibleMetricsChanged(); } }
    public bool ShowMemory { get => _showMemory; set { if (SetProperty(ref _showMemory, value)) NotifyVisibleMetricsChanged(); } }
    public bool ShowNetwork { get => _showNetwork; set { if (SetProperty(ref _showNetwork, value)) NotifyVisibleMetricsChanged(); } }
    public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public double Memory { get => _memory; private set => SetProperty(ref _memory, value); }
    public bool HasMemoryData => _memoryTotal > 0;
    public string MemoryLabel => HasMemoryData ? $"{Memory:0.0}%" : "N/A";
    public double Download { get => _download; private set => SetProperty(ref _download, value); }
    public double Upload { get => _upload; private set => SetProperty(ref _upload, value); }
    public string StatusLabel => IsEnabled ? "OVERLAY ON" : "PREVIEW ONLY";
    public double PreviewOpacity => IsEnabled ? 1 : 0.72;
    public string NetworkText => _networkSamples > 1 ? $"↓ {Download:0.0}  ↑ {Upload:0.0} Mbps" : "Collecting network counter baseline…";
    public string DownloadLabel => _networkSamples > 1 ? $"{Download:0.0} Mbps" : "N/A";
    public string UploadLabel => _networkSamples > 1 ? $"{Upload:0.0} Mbps" : "N/A";
    public bool HasVisibleMetrics => ShowCpu || ShowMemory || ShowNetwork;
    public bool HasNoVisibleMetrics => !HasVisibleMetrics;

    public OverlayModuleViewModel() : base("Overlay") { }
    public void AttachWindow(Action<bool> visibilityChanged, Action<bool> topmostChanged)
    {
        _visibilityChanged = visibilityChanged; _topmostChanged = topmostChanged;
        _topmostChanged(AlwaysOnTop); _visibilityChanged(IsEnabled);
    }
    public override void Update(SystemSnapshot snapshot)
    {
        Cpu = snapshot.CpuUsage; Memory = snapshot.MemoryUsage; _memoryTotal = snapshot.MemoryTotal; Download = snapshot.DownloadMbps; Upload = snapshot.UploadMbps; _networkSamples++;
        OnPropertyChanged(nameof(MemoryLabel)); OnPropertyChanged(nameof(HasMemoryData)); OnPropertyChanged(nameof(NetworkText)); OnPropertyChanged(nameof(DownloadLabel)); OnPropertyChanged(nameof(UploadLabel));
    }

    private void NotifyVisibleMetricsChanged() { OnPropertyChanged(nameof(HasVisibleMetrics)); OnPropertyChanged(nameof(HasNoVisibleMetrics)); }
}
public sealed class SoftwareModuleViewModel : MonitorModuleViewModel
{
    private IReadOnlyList<InstalledSoftwareItem> _allItems = Array.Empty<InstalledSoftwareItem>();
    private string _searchText = "", _statusText = "Scanning installed application entries…";
    private bool _isRefreshing;
    public ObservableCollection<InstalledSoftwareItem> Items { get; } = new();
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RefreshRows(); } }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsRefreshing { get => _isRefreshing; private set { if (SetProperty(ref _isRefreshing, value)) OnPropertyChanged(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsRefreshing;
    public int InstalledCount => _allItems.Count;
    public int VisibleCount => Items.Count;
    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => Items.Count == 0;
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText) ? "No installed application entries were found in the registry inventory." : "No applications match this filter. Try a shorter search.";
    public string InventorySummary => $"{InstalledCount} installed entries · {VisibleCount} shown";
    public ICommand RefreshCommand { get; }
    public SoftwareModuleViewModel() : base("Software")
    {
        RefreshCommand = new RelayCommand(_ => _ = RefreshInventoryAsync());
        _ = RefreshInventoryAsync();
    }
    public override void Update(SystemSnapshot snapshot) { }

    private async Task RefreshInventoryAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true; StatusText = "Reading installed-app registry entries…";
        try
        {
            var items = await Task.Run(ReadInstalledSoftware);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allItems = items; RefreshRows();
                StatusText = $"Read-only inventory · {items.Count} unique application entries";
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Inventory unavailable: {exception.GetType().Name}");
        }
        finally { await Dispatcher.UIThread.InvokeAsync(() => IsRefreshing = false); }
    }

    private void RefreshRows()
    {
        var rows = _allItems.Where(item => string.IsNullOrWhiteSpace(SearchText) || item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || item.Publisher.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
        Items.Clear(); foreach (var item in rows) Items.Add(item);
        OnPropertyChanged(nameof(InstalledCount)); OnPropertyChanged(nameof(VisibleCount)); OnPropertyChanged(nameof(HasItems)); OnPropertyChanged(nameof(HasNoItems)); OnPropertyChanged(nameof(EmptyStateText)); OnPropertyChanged(nameof(InventorySummary));
    }

    private static IReadOnlyList<InstalledSoftwareItem> ReadInstalledSoftware()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<InstalledSoftwareItem>();
        var items = new List<InstalledSoftwareItem>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var rootKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    using var root = rootKey.OpenSubKey(path);
                    if (root is null) continue;
                    foreach (var childName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var app = root.OpenSubKey(childName);
                            if (app is null || Convert.ToInt32(app.GetValue("SystemComponent", 0)) == 1) continue;
                            var name = Convert.ToString(app.GetValue("DisplayName"))?.Trim();
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            var publisher = Convert.ToString(app.GetValue("Publisher"))?.Trim() ?? "";
                            var version = Convert.ToString(app.GetValue("DisplayVersion"))?.Trim() ?? "";
                            var estimatedSize = double.TryParse(Convert.ToString(app.GetValue("EstimatedSize")), out var sizeKb) && sizeKb > 0 ? sizeKb / 1024d : (double?)null;
                            items.Add(new InstalledSoftwareItem(name, publisher, version, estimatedSize));
                        }
                        catch { /* An unreadable per-user entry should not hide the rest of the inventory. */ }
                    }
                }
            }
            catch { /* Some registry views are unavailable on a given Windows installation. */ }
        }
        return items.GroupBy(item => $"{item.Name}|{item.Version}", StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed record InstalledSoftwareItem(string Name, string Publisher, string Version, double? EstimatedSizeMegabytes)
{
    public string PublisherLabel => string.IsNullOrWhiteSpace(Publisher) ? "Publisher not reported" : Publisher;
    public string VersionLabel => string.IsNullOrWhiteSpace(Version) ? "Version not reported" : $"v{Version}";
    public string SizeLabel => EstimatedSizeMegabytes is > 0 ? $"{EstimatedSizeMegabytes:0} MB" : "Size not reported";
}

public sealed class SensorsModuleViewModel : MonitorModuleViewModel
{
    public ObservableCollection<SensorReadingViewModel> Items { get; } = new();
    public ObservableCollection<string> TypeFilters { get; } = new() { "All types" };
    private readonly Dictionary<string, SensorReadingViewModel> _sensorRows = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SensorReadingViewModel> _allItems = Array.Empty<SensorReadingViewModel>();
    private string _searchText = "", _selectedType = "All types";
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RefreshRows(); } }
    public string SelectedType { get => _selectedType; set { if (SetProperty(ref _selectedType, value)) RefreshRows(); } }
    public int TotalCount => _allItems.Count;
    public int HardwareCount => _allItems.Select(sensor => sensor.HardwareName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int TemperatureCount => _allItems.Count(sensor => sensor.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase));
    public string Summary => $"{TotalCount} exposed readings · {HardwareCount} hardware groups · {Items.Count} visible";
    public bool HasResults => Items.Count > 0;
    public bool HasNoResults => Items.Count == 0;
    public SensorsModuleViewModel() : base("Sensors") { }
    public override void Update(SystemSnapshot snapshot)
    {
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<SensorReadingViewModel>();
        foreach (var sensor in snapshot.Sensors.OrderBy(sensor => sensor.HardwareName).ThenBy(sensor => sensor.Type).ThenBy(sensor => sensor.Name))
        {
            var key = $"{sensor.HardwareName}\u001f{sensor.Type}\u001f{sensor.Name}";
            activeKeys.Add(key);
            if (_sensorRows.TryGetValue(key, out var row)) row.Update(sensor);
            else _sensorRows[key] = row = new SensorReadingViewModel(sensor);
            rows.Add(row);
        }
        foreach (var staleKey in _sensorRows.Keys.Where(key => !activeKeys.Contains(key)).ToArray()) _sensorRows.Remove(staleKey);
        _allItems = rows;
        var types = _allItems.Select(sensor => sensor.Type).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!TypeFilters.Skip(1).SequenceEqual(types, StringComparer.OrdinalIgnoreCase))
        {
            for (var index = TypeFilters.Count - 1; index > 0; index--) TypeFilters.RemoveAt(index);
            foreach (var type in types) TypeFilters.Add(type);
        }
        if (!TypeFilters.Contains(SelectedType, StringComparer.OrdinalIgnoreCase)) { _selectedType = "All types"; OnPropertyChanged(nameof(SelectedType)); }
        RefreshRows();
    }
    private void RefreshRows()
    {
        var filtered = _allItems.Where(sensor => (SelectedType == "All types" || sensor.Type.Equals(SelectedType, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(SearchText) || sensor.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || sensor.HardwareName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || sensor.Type.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
        ObservableCollectionReconciler.SetItems(Items, filtered.ToArray());
        OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(HardwareCount)); OnPropertyChanged(nameof(TemperatureCount));
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(HasResults)); OnPropertyChanged(nameof(HasNoResults));
    }
}
public sealed class DriverModuleViewModel : MonitorModuleViewModel
{
    private readonly KernelDriverLoader _loader = new();
    public ICommand LoadCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RemoveCommand { get; }
    public DriverModuleViewModel() : base("Driver")
    {
        LoadCommand = new RelayCommand(_ => _loader.TryLoad());
        ReloadCommand = new RelayCommand(_ => _loader.TryReload());
        StopCommand = new RelayCommand(_ => _loader.TryStopAndRemove());
        RemoveCommand = new RelayCommand(_ => _loader.TryStopAndRemove());
    }
    public override void Update(SystemSnapshot snapshot) { }
}
