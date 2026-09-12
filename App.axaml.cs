using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ExteraMonitor.ViewModels;
using ExteraMonitor.Views;
using ExteraMonitor.Services;

namespace ExteraMonitor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var captureCpu = Program.CpuCapturePath is not null;
            var viewModel = captureCpu
                ? new MainViewModel(SystemMetricsProviderFactory.Create(), new SqliteMetricsHistoryRepository(readOnly: true), persistUserData: false)
                : new MainViewModel();
            if (captureCpu)
            {
                viewModel.Cpu.ResetLayoutForPreview();
                viewModel.ActiveSection = "CPU";
            }

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                WindowState = Program.CpuCapturePath is null ? WindowState.Normal : WindowState.Maximized,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
