using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.Views;

public partial class MainWindow : Window
{
    private OverlayWindow? _overlayWindow;

    public MainWindow()
    {
        InitializeComponent();
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        _overlayWindow = new OverlayWindow { DataContext = viewModel.Overlay };
        viewModel.Overlay.AttachWindow(
            visible =>
            {
                if (_overlayWindow is null) return;
                if (visible) { if (!_overlayWindow.IsVisible) _overlayWindow.Show(); }
                else if (_overlayWindow.IsVisible) _overlayWindow.Hide();
            },
            topmost => { if (_overlayWindow is not null) _overlayWindow.Topmost = topmost; });

        if (Program.CapturePath is { } capturePath)
            _ = CaptureProofAsync(capturePath);
    }

    private async Task CaptureProofAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        if (Program.CaptureLoadingScreen)
        {
            while (DataContext is MainViewModel loadingViewModel && loadingViewModel.StartupStatus != "TELEMETRY ONLINE" && DateTime.UtcNow < deadline)
                await Task.Delay(250);
            await Task.Delay(350);
        }
        else
        {
            while (DataContext is MainViewModel readyViewModel && readyViewModel.IsStartupVisible && DateTime.UtcNow < deadline)
                await Task.Delay(250);
            if (Program.CaptureNavigationMotion && DataContext is MainViewModel motionViewModel)
            {
                await Dispatcher.UIThread.InvokeAsync(() => motionViewModel.ActiveSection = Program.CaptureSection);
                await Task.Delay(110);
            }
            else await Task.Delay(500);
        }

        try
        {
            var savedPath = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var scale = RenderScaling;
                var pixelSize = new PixelSize(
                    Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale)),
                    Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale)));
                using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
                bitmap.Render(this);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
                return path;
            }, DispatcherPriority.Render);

            Console.WriteLine($"CPU screenshot saved: {savedPath}");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                lifetime.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CPU screenshot failed: {exception.Message}");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                lifetime.Shutdown(-1);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _overlayWindow?.CloseFromOwner();
        _overlayWindow = null;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
