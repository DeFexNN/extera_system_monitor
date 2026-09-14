using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using ExteraMonitor.Models;
using ExteraMonitor.Services;
using ExteraMonitor.ViewModels.Modules;
namespace ExteraMonitor.ViewModels;
public sealed class MainViewModel : ViewModelBase
{
    private readonly ISystemMetricsProvider _provider; private readonly IMetricsHistoryRepository _history; private readonly bool _persistUserData; private readonly DispatcherTimer _timer; private readonly Dictionary<string, ViewModelBase> _modules; private DateTime? _updated; private string _active = "Overview"; private bool _live = true; private ViewModelBase _activeModule = null!; private int _refreshInProgress;
    private bool _isStartupVisible = true, _driverReady, _sensorsReady, _telemetryReady, _holdStartupOverlay, _finishingStartup;
    private int _readySamples;
    private double _startupOpacity = 1, _startupProgress = 8;
    private string _startupStatus = "STARTING KERNEL DRIVER", _startupDetail = "Connecting to ExteraMonitorDriver…";
    public OverviewModuleViewModel Overview { get; } = new(); public CpuModuleViewModel Cpu { get; } = new(); public MemoryModuleViewModel Memory { get; } = new(); public StorageModuleViewModel Storage { get; } = new(); public NetworkModuleViewModel Network { get; } = new(); public ProcessModuleViewModel Processes { get; } = new(); public OverlayModuleViewModel Overlay { get; } = new(); public SoftwareModuleViewModel Software { get; } = new(); public SensorsModuleViewModel Sensors { get; } = new(); public DriverModuleViewModel Driver { get; } = new(); public SettingsModuleViewModel Settings { get; }
    public ObservableCollection<NavigationItem> Navigation { get; } = new() { new("Overview", "⌂", true), new("CPU", "◒"), new("Memory", "▤"), new("Storage", "◫"), new("Network", "↗"), new("Processes", "≡"), new("Sensors", "°"), new("Driver", "⌁"), new("Settings", "⚙"), new("Overlay", "▣"), new("Software", "⊞") };
    public ViewModelBase ActiveModule { get => _activeModule; private set => SetProperty(ref _activeModule, value); }
    public string WorkstationName => $"{Environment.MachineName} / LOCAL";
    public string ActiveSection { get => _active; set { if (SetProperty(ref _active, value)) { foreach (var item in Navigation) item.IsSelected = item.Label == value; ActiveModule = _modules[value]; } } }
    public bool IsLive { get => _live; set { if (SetProperty(ref _live, value)) { OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(LiveActionLabel)); } } }
    public string StatusLabel => IsLive ? "LIVE SAMPLING" : "SAMPLING PAUSED";
    public string LiveActionLabel => IsLive ? "PAUSE" : "RESUME";
    public string LastUpdated => _updated is { } updated ? $"LAST SAMPLE {updated:HH:mm:ss}" : "WAITING FOR FIRST SAMPLE";
    public bool IsStartupVisible { get => _isStartupVisible; private set => SetProperty(ref _isStartupVisible, value); }
    public double StartupOpacity { get => _startupOpacity; private set => SetProperty(ref _startupOpacity, value); }
    public double StartupProgress { get => _startupProgress; private set => SetProperty(ref _startupProgress, value); }
    public string StartupStatus { get => _startupStatus; private set => SetProperty(ref _startupStatus, value); }
    public string StartupDetail { get => _startupDetail; private set => SetProperty(ref _startupDetail, value); }
    public string DriverStartupState => _driverReady ? "READY" : "CONNECTING";
    public string SensorsStartupState => _sensorsReady ? "READY" : "DISCOVERING";
    public string TelemetryStartupState => _telemetryReady ? "READY" : "SAMPLING";
    public ICommand SelectSectionCommand { get; } public ICommand ToggleLiveCommand { get; }
    public MainViewModel() : this(SystemMetricsProviderFactory.Create(), new SqliteMetricsHistoryRepository()) { }
    public MainViewModel(ISystemMetricsProvider provider) : this(provider, new SqliteMetricsHistoryRepository()) { }
    public MainViewModel(ISystemMetricsProvider provider, IMetricsHistoryRepository history, bool persistUserData = true)
    {
        _provider = provider;
        _history = history;
        _persistUserData = persistUserData;
        Settings = new SettingsModuleViewModel();
        Settings.ThemeChanged += Cpu.ApplyTheme;
        if (_history is ICpuCustomizationRepository customization)
        {
            try { Cpu.LoadCustomization(customization, persistUserData); }
            catch (Exception) { }
        }
        if (_history is IThemePaletteRepository theme)
        {
            try { Settings.Load(theme); }
            catch (Exception) { }
        }
        _modules = new() { ["Overview"] = Overview, ["CPU"] = Cpu, ["Memory"] = Memory, ["Storage"] = Storage, ["Network"] = Network, ["Processes"] = Processes, ["Sensors"] = Sensors, ["Driver"] = Driver, ["Settings"] = Settings, ["Overlay"] = Overlay, ["Software"] = Software };
        _activeModule = Overview;
        SelectSectionCommand = new RelayCommand(p => ActiveSection = p?.ToString() ?? "Overview");
        ToggleLiveCommand = new RelayCommand(_ => IsLive = !IsLive);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshAsync();
        RefreshAsync();
        _timer.Start();
    }

    private async void RefreshAsync()
    {
        if (!IsLive || Interlocked.Exchange(ref _refreshInProgress, 1) != 0) return;
        try
        {
            // Sensor enumeration, process enumeration and SQLite writes can block.
            // Keep all of that off Avalonia's UI thread so minimize/restore stays responsive.
            var snapshot = await Task.Run(_provider.GetSnapshot).ConfigureAwait(false);
            if (_persistUserData)
                try { _history.Store(snapshot); } catch (Exception) { }
            await Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(snapshot));
        }
        catch (Exception)
        {
            // A transient sensor/driver failure must not terminate the UI timer.
        }
        finally { Interlocked.Exchange(ref _refreshInProgress, 0); }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        if (!IsLive) return;
        Overview.Update(snapshot); Cpu.Update(snapshot); Memory.Update(snapshot); Storage.Update(snapshot); Network.Update(snapshot); Processes.Update(snapshot); Sensors.Update(snapshot); Overlay.Update(snapshot); Software.Update(snapshot);
        UpdateStartupState(snapshot);
        _updated = DateTime.Now;
        OnPropertyChanged(nameof(LastUpdated));
    }

    public void HoldStartupOverlayForCapture() => _holdStartupOverlay = true;

    private void UpdateStartupState(SystemSnapshot snapshot)
    {
        _driverReady = snapshot.TemperatureSource.Contains("ExteraMonitorDriver", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Sensors.Any(sensor => sensor.HardwareName.Contains("ExteraMonitorDriver", StringComparison.OrdinalIgnoreCase));
        var hasCpuTemperature = snapshot.CpuTemperature is > 0 and < 150;
        var hasGpuLoad = snapshot.Sensors.Any(sensor => IsGpu(sensor) && sensor.Type.Equals("Load", StringComparison.OrdinalIgnoreCase));
        var hasGpuTemperature = snapshot.Sensors.Any(sensor => IsGpu(sensor) && sensor.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase));
        _sensorsReady = hasCpuTemperature && hasGpuLoad && hasGpuTemperature;
        _telemetryReady = snapshot.Cores.Count > 0 && snapshot.MemoryTotal > 0 && snapshot.Disks.Count > 0 && snapshot.ProcessCount > 0;

        StartupProgress = 8 + (_driverReady ? 30 : 0) + (_sensorsReady ? 32 : 0) + (_telemetryReady ? 25 : 0) + (_readySamples > 0 ? 5 : 0);
        OnPropertyChanged(nameof(DriverStartupState)); OnPropertyChanged(nameof(SensorsStartupState)); OnPropertyChanged(nameof(TelemetryStartupState));

        if (!_driverReady)
        {
            StartupStatus = "STARTING KERNEL DRIVER";
            StartupDetail = "Loading ExteraMonitorDriver and opening the telemetry channel…";
        }
        else if (!_sensorsReady)
        {
            StartupStatus = "DISCOVERING HARDWARE SENSORS";
            StartupDetail = "Verifying CPU temperature and GPU driver telemetry…";
        }
        else if (!_telemetryReady)
        {
            StartupStatus = "SAMPLING SYSTEM COUNTERS";
            StartupDetail = "Synchronizing CPU cores, memory, storage and processes…";
        }
        else
        {
            _readySamples = Math.Min(2, _readySamples + 1);
            if (_readySamples < 2)
            {
                StartupStatus = "VALIDATING SIGNAL STABILITY";
                StartupDetail = "Confirmed hardware sample 1 of 2…";
            }
            else
            {
                StartupStatus = "TELEMETRY ONLINE";
                StartupDetail = "Driver and sensor channels are stable.";
                if (!_holdStartupOverlay && !_finishingStartup) _ = CompleteStartupAsync();
            }
        }

        if (!_driverReady || !_sensorsReady || !_telemetryReady) _readySamples = 0;
    }

    private async Task CompleteStartupAsync()
    {
        _finishingStartup = true;
        StartupProgress = 100;
        StartupStatus = "TELEMETRY ONLINE";
        StartupDetail = "Driver and sensor channels are stable.";
        await Task.Delay(300);
        StartupOpacity = 0;
        await Task.Delay(520);
        IsStartupVisible = false;
    }

    private static bool IsGpu(HardwareSensorMetric sensor) =>
        sensor.HardwareName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
        sensor.HardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
        sensor.HardwareName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
        sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase);
}
public sealed class NavigationItem : ViewModelBase { private bool _selected, _isHoverPreview; public string Label { get; } public string Icon { get; } public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); } public bool IsHoverPreview { get => _isHoverPreview; set => SetProperty(ref _isHoverPreview, value); } public NavigationItem(string label, string icon, bool selected = false) { Label = label; Icon = icon; _selected = selected; } }
public sealed class RelayCommand : ICommand { private readonly Action<object?> _execute; public RelayCommand(Action<object?> execute) => _execute = execute; public event EventHandler? CanExecuteChanged { add { } remove { } } public bool CanExecute(object? p) => true; public void Execute(object? p) => _execute(p); }
