using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using ExteraMonitor.Models;
using ExteraMonitor.Services;
using ExteraMonitor.ViewModels.Modules;
namespace ExteraMonitor.ViewModels;
public sealed class MainViewModel : ViewModelBase
{
    private readonly ISystemMetricsProvider _provider; private readonly IMetricsHistoryRepository _history; private readonly DispatcherTimer _timer; private readonly Dictionary<string, ViewModelBase> _modules; private DateTime _updated = DateTime.Now; private string _active = "Overview"; private bool _live = true; private ViewModelBase _activeModule = null!; private int _refreshInProgress;
    public OverviewModuleViewModel Overview { get; } = new(); public CpuModuleViewModel Cpu { get; } = new(); public MemoryModuleViewModel Memory { get; } = new(); public StorageModuleViewModel Storage { get; } = new(); public NetworkModuleViewModel Network { get; } = new(); public ProcessModuleViewModel Processes { get; } = new(); public OverlayModuleViewModel Overlay { get; } = new(); public SoftwareModuleViewModel Software { get; } = new(); public SensorsModuleViewModel Sensors { get; } = new(); public DriverModuleViewModel Driver { get; } = new(); public SettingsModuleViewModel Settings { get; }
    public ObservableCollection<NavigationItem> Navigation { get; } = new() { new("Overview", "⌂", true), new("CPU", "◒"), new("Memory", "▤"), new("Storage", "◫"), new("Network", "↗"), new("Processes", "≡"), new("Sensors", "°"), new("Driver", "⌁"), new("Settings", "⚙"), new("Overlay", "▣"), new("Software", "⊞") };
    public ViewModelBase ActiveModule { get => _activeModule; private set => SetProperty(ref _activeModule, value); }
    public string WorkstationName => $"{Environment.MachineName} / LOCAL";
    public string ActiveSection { get => _active; set { if (SetProperty(ref _active, value)) { foreach (var item in Navigation) item.IsSelected = item.Label == value; ActiveModule = _modules[value]; } } }
    public bool IsLive { get => _live; set { if (SetProperty(ref _live, value)) OnPropertyChanged(nameof(StatusLabel)); } } public string StatusLabel => IsLive ? "LIVE MONITORING" : "PAUSED"; public string LastUpdated => $"Updated {_updated:HH:mm:ss}";
    public ICommand SelectSectionCommand { get; } public ICommand ToggleLiveCommand { get; }
    public MainViewModel() : this(SystemMetricsProviderFactory.Create(), new SqliteMetricsHistoryRepository()) { }
    public MainViewModel(ISystemMetricsProvider provider) : this(provider, new SqliteMetricsHistoryRepository()) { }
    public MainViewModel(ISystemMetricsProvider provider, IMetricsHistoryRepository history) { _provider = provider; _history = history; Settings = new SettingsModuleViewModel(); if (_history is ICpuCustomizationRepository customization) { try { Cpu.LoadCustomization(customization); } catch (Exception) { } } if (_history is IThemePaletteRepository theme) { try { Settings.Load(theme); } catch (Exception) { } } _modules = new() { ["Overview"] = Overview, ["CPU"] = Cpu, ["Memory"] = Memory, ["Storage"] = Storage, ["Network"] = Network, ["Processes"] = Processes, ["Sensors"] = Sensors, ["Driver"] = Driver, ["Settings"] = Settings, ["Overlay"] = Overlay, ["Software"] = Software }; _activeModule = Overview; SelectSectionCommand = new RelayCommand(p => ActiveSection = p?.ToString() ?? "Overview"); ToggleLiveCommand = new RelayCommand(_ => IsLive = !IsLive); _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; _timer.Tick += (_, _) => RefreshAsync(); RefreshAsync(); _timer.Start(); }

    private async void RefreshAsync()
    {
        if (!IsLive || Interlocked.Exchange(ref _refreshInProgress, 1) != 0) return;
        try
        {
            // Sensor enumeration, process enumeration and SQLite writes can block.
            // Keep all of that off Avalonia's UI thread so minimize/restore stays responsive.
            var snapshot = await Task.Run(_provider.GetSnapshot).ConfigureAwait(false);
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
        _updated = DateTime.Now;
        OnPropertyChanged(nameof(LastUpdated));
    }
}
public sealed class NavigationItem : ViewModelBase { private bool _selected; public string Label { get; } public string Icon { get; } public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); } public NavigationItem(string label, string icon, bool selected = false) { Label = label; Icon = icon; _selected = selected; } }
public sealed class RelayCommand : ICommand { private readonly Action<object?> _execute; public RelayCommand(Action<object?> execute) => _execute = execute; public event EventHandler? CanExecuteChanged { add { } remove { } } public bool CanExecute(object? p) => true; public void Execute(object? p) => _execute(p); }
