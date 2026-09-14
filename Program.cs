using Avalonia;
using System;

namespace ExteraMonitor;

sealed class Program
{
    internal static string? CapturePath { get; private set; }
    internal static string CaptureSection { get; private set; } = "Overview";
    internal static bool CaptureDarkTheme { get; private set; }
    internal static bool CaptureWidgetLibrary { get; private set; }
    internal static bool CaptureNavigationHover { get; private set; }
    internal static bool CaptureLoadingScreen { get; private set; }
    internal static bool CaptureNormalWindow { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var captureIndex = Array.IndexOf(args, "--capture");
        if (captureIndex < 0) captureIndex = Array.IndexOf(args, "--capture-cpu");
        if (captureIndex >= 0)
        {
            var outputPath = captureIndex + 1 < args.Length && !args[captureIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[captureIndex + 1]
                : "ui-proof.png";
            CapturePath = Path.GetFullPath(outputPath);
            var sectionIndex = Array.IndexOf(args, "--capture-section");
            CaptureSection = sectionIndex >= 0 && sectionIndex + 1 < args.Length ? args[sectionIndex + 1] :
                args.Contains("--capture-cpu") ? "CPU" : "Overview";
            CaptureDarkTheme = args.Contains("--capture-dark");
            CaptureWidgetLibrary = args.Contains("--capture-widgets");
            CaptureNavigationHover = args.Contains("--capture-nav-hover");
            CaptureLoadingScreen = args.Contains("--capture-loading");
            CaptureNormalWindow = args.Contains("--capture-normal");
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(CapturePath is null ? args : []);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
