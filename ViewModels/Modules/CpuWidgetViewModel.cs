using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using ExteraMonitor.Models;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class CpuWidgetViewModel : ViewModelBase
{
    public const double MinimumWidth = 170;
    public const double MinimumHeight = 132;

    private double _x, _y, _width, _height;
    private int _gridColumn, _gridRow, _gridColumnSpan = 3, _gridRowSpan = 2;
    private int _style = 1;
    private string _valueText = "—";
    private string _statMinimum = "N/A", _statAverage = "N/A", _statMaximum = "N/A";
    private string _infoText = "Telemetry details are not available yet.";
    private bool _infoExpanded;
    private IReadOnlyList<double> _graphValues = Array.Empty<double>();

    public string Kind { get; }
    public IReadOnlyList<int> StyleOptions => Styles.Select(style => style.Index).ToArray();
    public string Title => Kind switch
    {
        "Load" => "PROCESSOR UTILIZATION", "Temperature" => "CPU PACKAGE TEMPERATURE", "Clock" => "CPU CLOCK",
        "Cores" => "LOGICAL CORE LOAD", "History" => "CPU HISTORY", "Power" => "CPU POWER", "Voltage" => "VOLTAGE",
        "Temperatures" => "TEMPERATURES", "PowerLimits" => "POWER LIMITS", "Frequency" => "FREQUENCY DISTRIBUTION", "CStates" => "C-STATE RESIDENCY",
        "Information" => "CPU INFORMATION", "Processes" => "TOP CPU PROCESSES", "PerCore" => "PER-CORE STATISTICS",
        "Sensors" => "ALL EXPOSED CPU SENSORS", _ => Kind.ToUpperInvariant()
    };
    public bool IsLoad => Kind == "Load";
    public bool IsTemperature => Kind == "Temperature";
    public bool IsClock => Kind == "Clock";
    public bool IsCores => Kind == "Cores";
    public bool IsHistory => Kind == "History";
    public bool IsPower => Kind == "Power";
    public bool IsVoltage => Kind == "Voltage";
    public bool IsTemperatures => Kind == "Temperatures";
    public bool IsPowerLimits => Kind == "PowerLimits";
    public bool IsFrequency => Kind == "Frequency";
    public bool IsCStates => Kind == "CStates";
    public bool IsInformation => Kind == "Information";
    public bool IsProcesses => Kind == "Processes";
    public bool IsPerCore => Kind == "PerCore";
    public bool IsSensors => Kind == "Sensors";
    public bool IsMetricCard => IsLoad || IsTemperature || IsClock;
    public double X { get => _x; set => SetProperty(ref _x, Math.Max(0, value)); }
    public double Y { get => _y; set => SetProperty(ref _y, Math.Max(0, value)); }
    public double Width { get => _width; set { if (SetProperty(ref _width, Math.Max(MinimumWidth, value))) NotifySize(); } }
    public double Height { get => _height; set { if (SetProperty(ref _height, Math.Max(MinimumHeight, value))) NotifySize(); } }
    public int GridColumn { get => _gridColumn; set => SetProperty(ref _gridColumn, Math.Clamp(value, 0, 11)); }
    public int GridRow { get => _gridRow; set => SetProperty(ref _gridRow, Math.Max(0, value)); }
    public int GridColumnSpan { get => _gridColumnSpan; set => SetProperty(ref _gridColumnSpan, Math.Clamp(value, 1, 12)); }
    public int GridRowSpan
    {
        get => _gridRowSpan;
        set
        {
            if (!SetProperty(ref _gridRowSpan, Math.Clamp(value, 1, 8))) return;
            OnPropertyChanged(nameof(IsCollapsed)); OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(CollapseActionText));
        }
    }
    public bool IsCollapsed => GridRowSpan == 1;
    public bool IsExpanded => !IsCollapsed;
    public string CollapseActionText => IsCollapsed ? "＋" : "−";
    public int CoreColumnCount
    {
        get
        {
            var maxColumns = Math.Max(1, Math.Min(8, CoreLoads.Count));
            return Math.Clamp((int)(Math.Max(1, Width - 32) / 90d), Math.Min(2, maxColumns), maxColumns);
        }
    }
    public double CoreGaugeSize => Math.Clamp(Math.Min((Width - 34) / Math.Max(1, CoreColumnCount) - 10, (Height - 88) / Math.Max(1, Math.Ceiling(Math.Max(1, CoreLoads.Count) / (double)Math.Max(1, CoreColumnCount))) - 8), 24, 76);
    public double CoreValueFontSize => Math.Clamp(CoreGaugeSize * 0.22, 9, 16);
    public string CoreCountText => $"{CoreLoads.Count} LOGICAL PROCESSORS";
    public string WidgetHeaderTitle => Width < 290 ? Kind switch
    {
        "Load" => "CPU LOAD", "Temperature" => "CPU TEMP", "Clock" => "CPU CLOCK", "Cores" => "CORE LOAD", "History" => "HISTORY",
        "Power" => "CPU POWER", "Voltage" => "VOLTAGE", "Temperatures" => "THERMALS", "PowerLimits" => "LIMITS", "Frequency" => "FREQ. BANDS",
        "CStates" => "C-STATES", "Information" => "CPU INFO", "Processes" => "TOP PROCESSES", "PerCore" => "PER-CORE", "Sensors" => "SENSORS", _ => Title
    } : Title;
    public double WidgetHeaderFontSize => Width < 230 ? 8 : Width < 330 ? 9 : 10;
    public bool ShowInlineAccentPicker => Width >= 250;
    public bool ShowInlineCollapse => Width >= 270;
    public bool ShowInlineInfo => Width >= 205;
    public double WidgetValueFontSize => Math.Clamp(Width * 0.105, 20, 42);
    public double GraphHeight => Math.Clamp(Height * 0.28, 38, 62);
    public double GraphMinimum => IsTemperature ? 20 : 0;
    public double GraphMaximum => IsTemperature ? 110 : IsClock ? 6000 : 100;
    public bool InfoExpanded { get => _infoExpanded; set => SetProperty(ref _infoExpanded, value); }
    public int Style
    {
        get => _style;
        set
        {
            var normalized = Styles.Any(style => style.Index == value) ? value : Styles.FirstOrDefault()?.Index ?? 1;
            if (!SetProperty(ref _style, normalized)) return;
            OnPropertyChanged(nameof(StyleLabel)); OnPropertyChanged(nameof(ActiveStyle)); NotifyVisuals(); Changed?.Invoke();
        }
    }
    public string StyleLabel => $"STYLE {Style}";
    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }
    public string StatMinimum { get => _statMinimum; private set => SetProperty(ref _statMinimum, value); }
    public string StatAverage { get => _statAverage; private set => SetProperty(ref _statAverage, value); }
    public string StatMaximum { get => _statMaximum; private set => SetProperty(ref _statMaximum, value); }
    public string InfoText { get => _infoText; private set => SetProperty(ref _infoText, value); }
    public IReadOnlyList<double> GraphValues { get => _graphValues; private set => SetProperty(ref _graphValues, value); }
    public ObservableCollection<CoreLoadViewModel> CoreLoads { get; } = new();
    public ObservableCollection<CpuWidgetStyleViewModel> Styles { get; } = new();
    public CpuWidgetStyleViewModel ActiveStyle => Styles.First(style => style.Index == Style);
    public IBrush SurfaceBrush => ActiveStyle.BackgroundBrush;
    public IBrush BorderBrush => ActiveStyle.BorderBrush;
    public IBrush TextBrush => ActiveStyle.TextBrush;
    public IBrush MutedBrush => ActiveStyle.MutedBrush;
    public IBrush GraphBrush => ActiveStyle.AccentBrush;
    public Color SelectedColor { get => ActiveStyle.AccentColor; set => ActiveStyle.AccentColor = value; }
    public string AccentColorHex => ToHex(SelectedColor);
    public ICommand CycleStyleCommand { get; }
    public ICommand AddStyleCommand { get; }
    public ICommand ToggleCollapseCommand { get; }
    public Action? Changed { get; set; }

    public CpuWidgetViewModel(string kind, double x, double y, double width, double height)
    {
        Kind = kind; _x = x; _y = y; _width = Math.Max(MinimumWidth, width); _height = Math.Max(MinimumHeight, height);
        CycleStyleCommand = new RelayCommand(_ => CycleStyle()); AddStyleCommand = new RelayCommand(_ => AddStyle());
        ToggleCollapseCommand = new RelayCommand(_ => ToggleCollapse());
        Styles.Add(new CpuWidgetStyleViewModel(1, "#E7F6F2", "#2C3333", "#395B64", "#A5C9CA", "#395B64"));
        Styles.Add(new CpuWidgetStyleViewModel(2, "#2C3333", "#E7F6F2", "#A5C9CA", "#395B64", "#A5C9CA"));
        Styles.Add(new CpuWidgetStyleViewModel(3, "#FFF1D6", "#523C2A", "#83684A", "#D6B779", "#B66A1C"));
        foreach (var styleModel in Styles) styleModel.Changed = StyleChanged;
    }

    public void SetAccent(Color color) => ActiveStyle.AccentColor = color;

    public void LoadStyles(IEnumerable<CpuWidgetStyleSettings> settings)
    {
        foreach (var setting in settings.OrderBy(setting => setting.Style))
        {
            var style = Styles.FirstOrDefault(item => item.Index == setting.Style);
            if (style is null) { style = Styles.OrderBy(item => item.Index).Last().Clone(setting.Style); style.Changed = StyleChanged; Styles.Add(style); OnPropertyChanged(nameof(StyleOptions)); }
            style.Load(setting.BackgroundHex, setting.TextHex, setting.MutedHex, setting.BorderHex, setting.AccentHex);
        }
        OnPropertyChanged(nameof(SelectedColor)); OnPropertyChanged(nameof(AccentColorHex)); NotifyVisuals();
    }

    public void Update(SystemSnapshot snapshot, IReadOnlyList<double> cpuValues, IReadOnlyList<double> temperatureValues, CpuTelemetryViewModel telemetry)
    {
        if (IsCores)
        {
            for (var index = 0; index < snapshot.Cores.Count; index++)
            {
                if (index < CoreLoads.Count) CoreLoads[index].Update(snapshot.Cores[index], ActiveStyle.AccentBrush, ActiveStyle.BorderBrush, ActiveStyle.TextBrush);
                else CoreLoads.Add(new CoreLoadViewModel(snapshot.Cores[index], ActiveStyle.AccentBrush, ActiveStyle.BorderBrush, ActiveStyle.TextBrush));
            }
            while (CoreLoads.Count > snapshot.Cores.Count) CoreLoads.RemoveAt(CoreLoads.Count - 1);
            OnPropertyChanged(nameof(CoreColumnCount)); OnPropertyChanged(nameof(CoreGaugeSize)); OnPropertyChanged(nameof(CoreCountText));
        }

        var values = IsLoad ? cpuValues : IsTemperature ? temperatureValues.Where(value => value > 0).ToArray() : IsClock ? telemetry.ClockHistory : IsPower ? telemetry.PowerHistory : IsVoltage ? telemetry.VoltageHistory : Array.Empty<double>();
        GraphValues = values;
        var valid = values.Where(double.IsFinite).ToArray();
        switch (Kind)
        {
            case "Load":
                ValueText = $"{snapshot.CpuUsage:0.0}%"; SetStats(valid, value => $"{value:0.0}%");
                InfoText = "Total processor utilization across all logical processors. History uses live samples from Windows."; break;
            case "Temperature":
                ValueText = snapshot.CpuTemperature > 0 ? $"{snapshot.CpuTemperature:0.0}°C" : "N/A"; SetStats(valid, value => $"{value:0.0}°C");
                InfoText = $"CPU package / Tctl temperature. Source: {snapshot.TemperatureSource}."; break;
            case "Clock":
                ValueText = telemetry.CurrentClock; SetStats(valid, value => $"{value / 1000d:0.00} GHz");
                InfoText = "Average effective frequency across the logical processors with a valid APERF/MPERF reading."; break;
            case "Power": ValueText = telemetry.PackagePower; InfoText = "Package, core, and SoC power reported by hardware sensors or the CPU energy counters."; break;
            case "Voltage": ValueText = telemetry.CoreVoltage; InfoText = "Current CPU rail voltage from the exposed sensor. Minimum and maximum are observed values in this session."; break;
            case "History": ValueText = "LIVE"; InfoText = "Selected CPU metrics over time. Toggle series and choose a time range in the chart header."; break;
            case "Cores": InfoText = "Each gauge is one logical processor. The ring and rounded mini-blocks show current utilization."; break;
            case "PowerLimits": InfoText = "PPT is reported power; TDC and EDC are reported current. A limit bar is shown only when the platform exposes a real limit."; break;
            case "CStates": InfoText = "Idle-state residency is shown only when the hardware or driver exposes C-state counters. Otherwise the rows report N/A."; break;
            case "Sensors": InfoText = "Every currently exposed CPU sensor, grouped by sensor type. Missing hardware channels are omitted."; break;
            default: InfoText = $"Live {Title.ToLowerInvariant()} telemetry."; break;
        }
        OnPropertyChanged(nameof(WidgetHeaderTitle)); OnPropertyChanged(nameof(WidgetValueFontSize)); OnPropertyChanged(nameof(GraphHeight));
    }

    private void SetStats(IReadOnlyList<double> values, Func<double, string> format)
    {
        StatMinimum = values.Count == 0 ? "N/A" : format(values.Min());
        StatAverage = values.Count == 0 ? "N/A" : format(values.Average());
        StatMaximum = values.Count == 0 ? "N/A" : format(values.Max());
    }

    private void CycleStyle() { var options = StyleOptions; var index = Array.IndexOf(options.ToArray(), Style); Style = options[(index + 1) % options.Count]; }
    private void ToggleCollapse() { GridRowSpan = IsCollapsed ? (Kind is "Cores" or "History" ? 2 : Kind == "PerCore" ? 3 : 1) : 1; Changed?.Invoke(); }
    private void AddStyle() { var next = Styles.Count == 0 ? 1 : Styles.Max(style => style.Index) + 1; var style = ActiveStyle.Clone(next); style.Changed = StyleChanged; Styles.Add(style); OnPropertyChanged(nameof(StyleOptions)); Style = next; Changed?.Invoke(); }
    private void StyleChanged() { NotifyVisuals(); }
    private void NotifyVisuals()
    {
        OnPropertyChanged(nameof(SurfaceBrush)); OnPropertyChanged(nameof(BorderBrush)); OnPropertyChanged(nameof(TextBrush)); OnPropertyChanged(nameof(MutedBrush)); OnPropertyChanged(nameof(GraphBrush)); OnPropertyChanged(nameof(SelectedColor)); OnPropertyChanged(nameof(AccentColorHex));
        if (IsCores) foreach (var core in CoreLoads) core.UpdateBrushes(ActiveStyle.AccentBrush, ActiveStyle.BorderBrush, ActiveStyle.TextBrush);
    }
    private void NotifySize() { OnPropertyChanged(nameof(CoreColumnCount)); OnPropertyChanged(nameof(CoreGaugeSize)); OnPropertyChanged(nameof(CoreValueFontSize)); OnPropertyChanged(nameof(WidgetHeaderFontSize)); OnPropertyChanged(nameof(WidgetHeaderTitle)); OnPropertyChanged(nameof(ShowInlineAccentPicker)); OnPropertyChanged(nameof(ShowInlineCollapse)); OnPropertyChanged(nameof(ShowInlineInfo)); OnPropertyChanged(nameof(WidgetValueFontSize)); OnPropertyChanged(nameof(GraphHeight)); }
    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
