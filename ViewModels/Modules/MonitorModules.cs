using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using ExteraMonitor.Models;
using ExteraMonitor.Services;
namespace ExteraMonitor.ViewModels.Modules;
public abstract class MonitorModuleViewModel : ViewModelBase { public string ModuleName { get; } protected MonitorModuleViewModel(string name) => ModuleName = name; public abstract void Update(SystemSnapshot snapshot); }
public sealed class OverviewModuleViewModel : MonitorModuleViewModel { private double _cpu, _memory, _storage, _network; public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); } public double Memory { get => _memory; private set => SetProperty(ref _memory, value); } public double Storage { get => _storage; private set => SetProperty(ref _storage, value); } public double Network { get => _network; private set => SetProperty(ref _network, value); } public string Cores => $"{Environment.ProcessorCount} logical cores"; public string Uptime { get; private set; } = "—"; public OverviewModuleViewModel() : base("Overview") { } public override void Update(SystemSnapshot s) { Cpu = s.CpuUsage; Memory = s.MemoryUsage; Storage = s.StorageUsage; Network = s.DownloadMbps; Uptime = $"{(int)s.Uptime.TotalHours}h {s.Uptime.Minutes:00}m"; OnPropertyChanged(nameof(Uptime)); } }
public sealed class CpuModuleViewModel : MonitorModuleViewModel
{
    private static readonly string[] WidgetKinds = ["Load", "Temperature", "Clock", "Cores", "History", "Power", "Voltage", "Temperatures", "PowerLimits", "Frequency", "CStates", "Information", "Processes", "PerCore", "Sensors"];
    private const int HistoryLimit = 1800;
    private double _usage, _temperature;
    private string _temperatureSource = "Sensor scan pending";
    private Color _accentColor = Color.Parse("#395B64");
    private string _selectedAccentPreset = "TEAL";
    private ICpuCustomizationRepository? _customizationRepository;
    private string _historyRange = "10m";
    private double _lastSurfaceWidth;
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
    public string HistoryRange { get => _historyRange; set { if (SetProperty(ref _historyRange, HistoryRanges.Contains(value) ? value : "10m")) { OnPropertyChanged(nameof(HistorySampleCount)); OnPropertyChanged(nameof(HistoryLoad)); OnPropertyChanged(nameof(HistoryTemperature)); OnPropertyChanged(nameof(HistoryClock)); OnPropertyChanged(nameof(HistoryPower)); SaveCustomization(); } } }
    public int HistorySampleCount => HistoryRange switch { "1m" => 60, "5m" => 300, "30m" => 1800, _ => 600 };
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
        foreach (var kind in WidgetKinds)
            AddWidget(kind, save: false);
    }

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
        if (settings.Widgets.Count > 0)
        {
            var oldPixelLayout = settings.Widgets.Any(widget => widget.X > 12 || widget.Y > 40 || widget.Width > 12 || widget.Height > 8);
            Widgets.Clear();
            if (oldPixelLayout)
            {
                foreach (var kind in WidgetKinds) AddWidget(kind, save: false);
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
        OnPropertyChanged(nameof(HistoryRange)); OnPropertyChanged(nameof(HistorySampleCount));
        OnPropertyChanged(nameof(ShowHistoryLoad)); OnPropertyChanged(nameof(ShowHistoryTemperature)); OnPropertyChanged(nameof(ShowHistoryClock)); OnPropertyChanged(nameof(ShowHistoryPower));
        OnPropertyChanged(nameof(SensorsExpanded)); OnPropertyChanged(nameof(SensorsCollapsed));
        SelectedAccentPreset = Accents.Any(accent => accent.Name == _selectedAccentPreset) ? _selectedAccentPreset : "TEAL";
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
        SensorPreview.Clear();
        foreach (var sensor in Telemetry.AllSensors.Take(6)) SensorPreview.Add(sensor);
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
        widget.Changed = SaveCustomization;
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
        const double margin = 12, gap = 12, rowUnit = 104;
        var usableWidth = Math.Max(160, surfaceWidth - margin * 2);
        var columns = usableWidth < 480 ? 2 : usableWidth < 780 ? 6 : 12;
        var cellWidth = Math.Max(1, (usableWidth - gap * (columns - 1)) / columns);
        var occupied = new HashSet<(int Column, int Row)>();
        var placements = new List<(CpuWidgetViewModel Widget, int Column, int Row, int Span, int Rows)>();
        var ordered = Widgets.OrderBy(widget => widget.GridRow).ThenBy(widget => widget.GridColumn).ThenBy(widget => Array.IndexOf(WidgetKinds, widget.Kind)).ToArray();
        foreach (var widget in ordered)
        {
            var desired = ResponsivePlacement(widget, columns);
            var span = Math.Clamp(desired.Span, 1, columns);
            var rows = widget.IsSensors && SensorsExpanded ? Math.Max(4, desired.Rows) : desired.Rows;
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
        const double margin = 12, gap = 12, rowUnit = 104;
        var columns = surfaceWidth - margin * 2 < 480 ? 2 : surfaceWidth - margin * 2 < 780 ? 6 : 12;
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
        "Cores" => (0, 2, 6, 3), "History" => (6, 2, 6, 3),
        "Power" => (0, 5, 2, 2), "Voltage" => (2, 5, 2, 2), "Temperatures" => (4, 5, 2, 2),
        "PowerLimits" => (6, 5, 2, 2), "Frequency" => (8, 5, 4, 2),
        "CStates" => (0, 7, 3, 2), "Information" => (3, 7, 3, 2), "Processes" => (6, 7, 3, 2), "PerCore" => (9, 7, 3, 2),
        "Sensors" => (0, 9, 12, 2), _ => (0, 10, 4, 2)
    };

    private (int Column, int Row, int Span, int Rows) ResponsivePlacement(CpuWidgetViewModel widget, int columns)
    {
        if (columns == 12) return (widget.GridColumn, widget.GridRow, widget.GridColumnSpan, widget.GridRowSpan);
        var rows = widget.Kind switch { "Cores" or "History" => 3, "Sensors" => SensorsExpanded ? 4 : 2, _ => 2 };
        if (columns == 6)
        {
            var span = widget.Kind is "Cores" or "History" or "PerCore" or "Sensors" or "Frequency" ? 6 : 3;
            return (0, 0, span, rows);
        }
        return (0, 0, 2, rows);
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
        widget.GridColumn = 0;
        widget.GridRow = Widgets.Select(item => item.GridRow + item.GridRowSpan).DefaultIfEmpty(0).Max();
        widget.GridColumnSpan = widget.Kind is "Cores" or "History" or "PerCore" or "Frequency" ? 6 : widget.Kind == "Sensors" ? 12 : 3;
        widget.GridRowSpan = widget.Kind is "Cores" or "History" or "PerCore" or "Frequency" ? 3 : 2;
    }

    private void ResetLayout()
    {
        var oldWidgets = Widgets.ToDictionary(widget => widget.Kind, StringComparer.OrdinalIgnoreCase);
        Widgets.Clear();
        foreach (var kind in WidgetKinds)
        {
            AddWidget(kind, save: false);
            if (!oldWidgets.TryGetValue(kind, out var old)) continue;
            var restored = Widgets.Last();
            restored.Style = old.Style;
            restored.LoadStyles(old.Styles.Select(style => new CpuWidgetStyleSettings(kind, style.Index, style.BackgroundHex, style.TextHex, style.MutedHex, style.BorderHex, style.AccentHex)));
        }
        _historyRange = "10m";
        _showHistoryLoad = _showHistoryTemperature = _showHistoryClock = _showHistoryPower = true;
        _sensorsExpanded = false;
        OnPropertyChanged(nameof(HistoryRange)); OnPropertyChanged(nameof(ShowHistoryLoad)); OnPropertyChanged(nameof(ShowHistoryTemperature));
        OnPropertyChanged(nameof(ShowHistoryClock)); OnPropertyChanged(nameof(ShowHistoryPower)); OnPropertyChanged(nameof(SensorsExpanded)); OnPropertyChanged(nameof(SensorsCollapsed));
        NotifyAddAvailability();
        foreach (var item in WidgetLibrary) item.Refresh(true);
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
        SaveCustomization();
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
public sealed class MemoryModuleViewModel : MonitorModuleViewModel { private double _usage, _totalGigabytes; public double Usage { get => _usage; private set => SetProperty(ref _usage, value); } public double TotalGigabytes { get => _totalGigabytes; private set => SetProperty(ref _totalGigabytes, value); } public string Used => $"{Usage * TotalGigabytes / 100:0.0} GB"; public string Available => $"{Math.Max(0, TotalGigabytes - Usage * TotalGigabytes / 100):0.0} GB"; public string Total => $"{TotalGigabytes:0.0} GB total"; public MemoryModuleViewModel() : base("Memory") { } public override void Update(SystemSnapshot s) { Usage = s.MemoryUsage; TotalGigabytes = s.MemoryTotal; OnPropertyChanged(nameof(Used)); OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(Total)); } }
public sealed class StorageModuleViewModel : MonitorModuleViewModel { private double _usage, _totalGigabytes; private string _primaryName = "No fixed disk"; public ObservableCollection<DiskMetric> Disks { get; } = new(); public double Usage { get => _usage; private set => SetProperty(ref _usage, value); } public double TotalGigabytes { get => _totalGigabytes; private set => SetProperty(ref _totalGigabytes, value); } public string PrimaryName { get => _primaryName; private set => SetProperty(ref _primaryName, value); } public string Used => $"{Usage * TotalGigabytes / 100:0.0} GB"; public string Total => $"{TotalGigabytes:0.0} GB total"; public StorageModuleViewModel() : base("Storage") { } public override void Update(SystemSnapshot s) { Usage = s.StorageUsage; TotalGigabytes = s.StorageTotal; var primary = s.Disks.FirstOrDefault(); PrimaryName = primary is null ? "No fixed disk" : $"Primary volume / {primary.Name}"; Disks.Clear(); foreach (var disk in s.Disks) Disks.Add(disk); OnPropertyChanged(nameof(Used)); OnPropertyChanged(nameof(Total)); } }
public sealed class NetworkModuleViewModel : MonitorModuleViewModel { private double _download, _upload; public double Download { get => _download; private set => SetProperty(ref _download, value); } public double Upload { get => _upload; private set => SetProperty(ref _upload, value); } public NetworkModuleViewModel() : base("Network") { } public override void Update(SystemSnapshot s) { Download = s.DownloadMbps; Upload = s.UploadMbps; } }
public sealed class ProcessModuleViewModel : MonitorModuleViewModel { public ObservableCollection<ProcessInfo> Items { get; } = new(); public int Count { get; private set; } public ProcessModuleViewModel() : base("Processes") { } public override void Update(SystemSnapshot s) { Items.Clear(); foreach (var item in s.Processes) Items.Add(item); Count = s.ProcessCount; OnPropertyChanged(nameof(Count)); } }
public sealed class OverlayModuleViewModel : MonitorModuleViewModel
{
    private bool _isEnabled = true, _alwaysOnTop = true, _showCpu = true, _showMemory = true, _showNetwork = true; private double _cpu, _memory, _download;
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); } public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); } public bool ShowCpu { get => _showCpu; set => SetProperty(ref _showCpu, value); } public bool ShowMemory { get => _showMemory; set => SetProperty(ref _showMemory, value); } public bool ShowNetwork { get => _showNetwork; set => SetProperty(ref _showNetwork, value); }
    public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); } public double Memory { get => _memory; private set => SetProperty(ref _memory, value); } public double Download { get => _download; private set => SetProperty(ref _download, value); }
    public OverlayModuleViewModel() : base("Overlay") { } public override void Update(SystemSnapshot s) { Cpu = s.CpuUsage; Memory = s.MemoryUsage; Download = s.DownloadMbps; }
}
public sealed class SoftwareModuleViewModel : MonitorModuleViewModel
{
    public ObservableCollection<SoftwareFeature> Features { get; } = new() { new("Software inventory", "Installed application and version discovery.", "Planned"), new("Update advisor", "Surface available updates for chosen tools.", "Planned"), new("Service inspector", "Review local service health and startup behavior.", "Planned") };
    public SoftwareModuleViewModel() : base("Software") { } public override void Update(SystemSnapshot snapshot) { }
}
public sealed class SensorsModuleViewModel : MonitorModuleViewModel
{
    public ObservableCollection<HardwareSensorMetric> Items { get; } = new();
    public SensorsModuleViewModel() : base("Sensors") { }
    public override void Update(SystemSnapshot snapshot) { Items.Clear(); foreach (var sensor in snapshot.Sensors.OrderBy(sensor => sensor.HardwareName).ThenBy(sensor => sensor.Type).ThenBy(sensor => sensor.Name)) Items.Add(sensor); }
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
