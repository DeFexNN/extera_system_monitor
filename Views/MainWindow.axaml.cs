using Avalonia.Controls;
using Avalonia.Input;
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
