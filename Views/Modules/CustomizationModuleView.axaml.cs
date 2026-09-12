using Avalonia.Controls;
using ExteraMonitor.ViewModels.Modules;

namespace ExteraMonitor.Views.Modules;

public partial class CustomizationModuleView : UserControl
{
    public CustomizationModuleView() => InitializeComponent();

    private void AccentColorApplied(object? sender, EventArgs e)
    {
        if (sender is Control { DataContext: AccentColorViewModel accent }) accent.Commit();
    }

    private void WidgetColorApplied(object? sender, EventArgs e)
    {
        if (DataContext is CustomizationModuleViewModel viewModel) viewModel.Save();
    }

    private void StyleColorApplied(object? sender, EventArgs e)
    {
        if (DataContext is CustomizationModuleViewModel viewModel) viewModel.Save();
    }
}
