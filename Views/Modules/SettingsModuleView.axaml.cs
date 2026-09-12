using Avalonia.Controls;
using ExteraMonitor.ViewModels.Modules;

namespace ExteraMonitor.Views.Modules;

public partial class SettingsModuleView : UserControl
{
    public SettingsModuleView() => InitializeComponent();

    private void ThemeColorApplied(object? sender, EventArgs e)
    {
        if (sender is Control { DataContext: ThemeColorViewModel color }) color.Commit();
    }
}
