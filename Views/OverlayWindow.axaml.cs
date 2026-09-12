using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ExteraMonitor.ViewModels.Modules;

namespace ExteraMonitor.Views;

public partial class OverlayWindow : Window
{
    private bool _closingWithOwner;

    public OverlayWindow()
    {
        InitializeComponent();
        Closing += OverlayWindow_Closing;
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Hide_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OverlayModuleViewModel viewModel) viewModel.IsEnabled = false;
    }

    private void OverlayWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingWithOwner) return;
        e.Cancel = true;
        if (DataContext is OverlayModuleViewModel viewModel) viewModel.IsEnabled = false;
    }

    public void CloseFromOwner()
    {
        _closingWithOwner = true;
        Close();
    }
}
