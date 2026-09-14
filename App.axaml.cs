using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ExteraMonitor.ViewModels;
using ExteraMonitor.Views;
using ExteraMonitor.Services;
using System.Threading;
using System.Threading.Tasks;

namespace ExteraMonitor;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private int _driverFailureShutdownQueued;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            KernelDriverLoader.DriverLoadFailed += OnDriverLoadFailed;
            var captureUi = Program.CapturePath is not null;
            var viewModel = captureUi
                ? new MainViewModel(Program.CaptureLoadingScreen ? new SimulatedMetricsProvider() : SystemMetricsProviderFactory.Create(), new SqliteMetricsHistoryRepository(readOnly: true), persistUserData: false)
                : new MainViewModel();
            if (captureUi)
            {
                if (Program.CaptureDarkTheme) viewModel.Settings.ApplyPreviewTheme("Dark");
                viewModel.ActiveSection = Program.CaptureNavigationMotion ? "Overview" : Program.CaptureSection;
                if (Program.CaptureNavigationHover)
                    viewModel.Navigation.First(item => item.Label == "CPU").IsHoverPreview = true;
                if (Program.CaptureLoadingScreen) viewModel.HoldStartupOverlayForCapture();
                if (Program.CaptureSection == "CPU")
                {
                    viewModel.Cpu.ResetLayoutForPreview();
                    viewModel.Cpu.SetWidgetLibraryVisible(Program.CaptureWidgetLibrary);
                }
            }

            _mainViewModel = viewModel;
            _mainWindow = new MainWindow
            {
                DataContext = viewModel,
                WindowState = Program.CapturePath is null || Program.CaptureNormalWindow ? WindowState.Normal : WindowState.Maximized,
                IsTrayModeEnabled = !captureUi
            };
            desktop.MainWindow = _mainWindow;
            if (captureUi) TrayIcon.SetIcons(this, null);
            else desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDriverLoadFailed()
    {
        if (Interlocked.Exchange(ref _driverFailureShutdownQueued, 1) != 0) return;
        KernelDriverLoader.DriverLoadFailed -= OnDriverLoadFailed;
        Dispatcher.UIThread.Post(() => _ = ShowDriverFailureWindowAsync());
    }

    private async Task ShowDriverFailureWindowAsync()
    {
        var telegramFlush = Task.Run(() => DriverDiagnostics.FlushTelegram(TimeSpan.FromSeconds(20)));
        try
        {
            if (_mainWindow is { } owner)
                await new DriverFailureWindow().ShowDialog(owner);
        }
        finally
        {
            await telegramFlush;
            _mainWindow?.PrepareForApplicationExit();
            _desktop?.Shutdown(1);
        }
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e) => ShowMainWindow();
    private void TrayOpen_Click(object? sender, EventArgs e) => ShowMainWindow();

    private void TrayToggleSampling_Click(object? sender, EventArgs e)
    {
        if (_mainViewModel is { } viewModel) viewModel.IsLive = !viewModel.IsLive;
    }

    private void TrayExit_Click(object? sender, EventArgs e)
    {
        _mainWindow?.PrepareForApplicationExit();
        TrayIcon.SetIcons(this, null);
        _desktop?.Shutdown(0);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.RestoreFromTray();
    }
}
