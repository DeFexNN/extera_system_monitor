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
            var captureUi = Program.CapturePath is not null;
            var viewModel = captureUi
                ? new MainViewModel(SystemMetricsProviderFactory.Create(), new SqliteMetricsHistoryRepository(readOnly: true), persistUserData: false)
                : new MainViewModel();
            if (captureUi)
            {
                if (Program.CaptureDarkTheme) viewModel.Settings.ApplyPreviewTheme("Dark");
                viewModel.ActiveSection = Program.CaptureSection;
                if (Program.CaptureNavigationHover)
                    viewModel.Navigation.First(item => item.Label == "CPU").IsHoverPreview = true;
                if (Program.CaptureLoadingScreen) viewModel.HoldStartupOverlayForCapture();
                if (Program.CaptureSection == "CPU")
                {
                    viewModel.Cpu.ResetLayoutForPreview();
                    viewModel.Cpu.SetWidgetLibraryVisible(Program.CaptureWidgetLibrary);
                }
            }

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                WindowState = Program.CapturePath is null || Program.CaptureNormalWindow ? WindowState.Normal : WindowState.Maximized,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
