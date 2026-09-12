using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ExteraMonitor.ViewModels.Modules;

namespace ExteraMonitor.Views.Modules;

public partial class CpuModuleView : UserControl
{
    private CpuWidgetViewModel? _draggingWidget;
    private CpuWidgetViewModel? _resizingWidget;
    private Point _dragOffset;
    private Point _resizeStartPointer;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public CpuModuleView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateLibraryVisibility();
    }

    private void DashboardArea_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is CpuModuleViewModel viewModel)
            viewModel.SetWidgetLibraryVisible(e.NewSize.Width >= 1250);
    }

    private void UpdateLibraryVisibility()
    {
        if (DataContext is CpuModuleViewModel viewModel)
            viewModel.SetWidgetLibraryVisible(DashboardArea.Bounds.Width >= 1250);
    }

    private void WidgetDrag_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CpuWidgetViewModel widget }) return;
        var position = e.GetPosition(WidgetSurface);
        _draggingWidget = widget;
        _dragOffset = new Point(position.X - widget.X, position.Y - widget.Y);
        e.Pointer.Capture(WidgetSurface);
        e.Handled = true;
    }

    private void WidgetInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CpuWidgetViewModel widget })
            widget.InfoExpanded = !widget.InfoExpanded;
    }

    private void WidgetSurface_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingWidget is not null)
        {
            var resizePosition = e.GetPosition(WidgetSurface);
            var maxWidth = Math.Max(CpuWidgetViewModel.MinimumWidth, WidgetSurface.Bounds.Width - _resizingWidget.X);
            var maxHeight = Math.Max(CpuWidgetViewModel.MinimumHeight, 1200 - _resizingWidget.Y);
            _resizingWidget.Width = Math.Clamp(_resizeStartWidth + resizePosition.X - _resizeStartPointer.X, CpuWidgetViewModel.MinimumWidth, maxWidth);
            _resizingWidget.Height = Math.Clamp(_resizeStartHeight + resizePosition.Y - _resizeStartPointer.Y, CpuWidgetViewModel.MinimumHeight, maxHeight);
            return;
        }
        if (_draggingWidget is null) return;
        var position = e.GetPosition(WidgetSurface);
        var maxX = Math.Max(0, WidgetSurface.Bounds.Width - _draggingWidget.Width);
        var maxY = Math.Max(0, WidgetSurface.Bounds.Height - _draggingWidget.Height);
        _draggingWidget.X = Math.Clamp(position.X - _dragOffset.X, 0, maxX);
        _draggingWidget.Y = Math.Clamp(position.Y - _dragOffset.Y, 0, maxY);
    }

    private void WidgetSurface_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not CpuModuleViewModel viewModel) return;
        viewModel.ResizeWidgets(e.NewSize.Width, e.NewSize.Height);
    }

    private void WidgetSurface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingWidget is not null)
        {
            if (DataContext is CpuModuleViewModel resizingViewModel)
            {
                resizingViewModel.CaptureWidgetGridLayout(WidgetSurface.Bounds.Width);
                resizingViewModel.ResizeWidgets(WidgetSurface.Bounds.Width, WidgetSurface.Bounds.Height);
            }
            e.Pointer.Capture(null);
            _resizingWidget = null;
            return;
        }
        if (_draggingWidget is null) return;
        if (DataContext is CpuModuleViewModel viewModel)
        {
            viewModel.CaptureWidgetGridLayout(WidgetSurface.Bounds.Width);
            viewModel.ResizeWidgets(WidgetSurface.Bounds.Width, WidgetSurface.Bounds.Height);
        }
        e.Pointer.Capture(null);
        _draggingWidget = null;
    }

    private void WidgetResize_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CpuWidgetViewModel widget }) return;
        _resizingWidget = widget;
        _resizeStartPointer = e.GetPosition(WidgetSurface);
        _resizeStartWidth = widget.Width;
        _resizeStartHeight = widget.Height;
        e.Pointer.Capture(WidgetSurface);
        e.Handled = true;
    }

    private void RemoveWidget_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CpuWidgetViewModel widget } && DataContext is CpuModuleViewModel viewModel)
            viewModel.RemoveWidgetCommand.Execute(widget);
    }

    private void WidgetColorApplied(object? sender, EventArgs e)
    {
        if (DataContext is CpuModuleViewModel viewModel) viewModel.SaveCustomization();
    }

    private void AccentColorApplied(object? sender, EventArgs e)
    {
        if (DataContext is CpuModuleViewModel viewModel) viewModel.SaveCustomization();
    }

    private void AccentPresetApplied(object? sender, EventArgs e)
    {
        if (sender is Control { DataContext: AccentColorViewModel accent }) accent.Commit();
    }

    private void StyleColorApplied(object? sender, EventArgs e)
    {
        if (DataContext is CpuModuleViewModel viewModel) viewModel.SaveCustomization();
    }

    private void SensorsToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CpuModuleViewModel viewModel) return;
        viewModel.SensorsExpanded = !viewModel.SensorsExpanded;
        viewModel.ResizeWidgets(WidgetSurface.Bounds.Width, WidgetSurface.Bounds.Height);
    }

    private void HistorySeries_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CpuModuleViewModel viewModel || sender is not Control { Tag: string series }) return;
        switch (series)
        {
            case "Load": viewModel.ShowHistoryLoad = !viewModel.ShowHistoryLoad; break;
            case "Temperature": viewModel.ShowHistoryTemperature = !viewModel.ShowHistoryTemperature; break;
            case "Clock": viewModel.ShowHistoryClock = !viewModel.ShowHistoryClock; break;
            case "Power": viewModel.ShowHistoryPower = !viewModel.ShowHistoryPower; break;
        }
    }
}
