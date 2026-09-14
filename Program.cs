using Avalonia;
using System;
using ExteraMonitor.Services;

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
    internal static bool CaptureNavigationMotion { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--driver-self-test"))
        {
            RunDriverSelfTest();
            return;
        }

        if (args.Contains("--driver-security-diagnostics"))
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Driver security diagnostics are available on Windows only.");
                Environment.ExitCode = 2;
                return;
            }

            Console.WriteLine(DriverSecurityDiagnostics.Collect());
            return;
        }

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
            CaptureNavigationMotion = args.Contains("--capture-nav-motion");
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

    private static void RunDriverSelfTest()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Driver self-test is available on Windows only.");
                Environment.ExitCode = 2;
                return;
            }

            using var provider = new KernelTemperatureProvider();
            var reading = provider.Read();
            var temperature = reading.Sensors.FirstOrDefault(sensor =>
                sensor.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase) &&
                sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"Source: {reading.TemperatureSource}");
            if (temperature is null)
            {
                Console.Error.WriteLine("Driver self-test failed: no CPU package temperature was returned.");
                Environment.ExitCode = 2;
                return;
            }

            Console.WriteLine($"CPU package temperature: {temperature.Value:0.0} {temperature.Unit.Replace("°", string.Empty, StringComparison.Ordinal)}");
            Console.WriteLine($"Driver diagnostic log: {DriverDiagnosticsPath()}");
            Environment.ExitCode = 0;
        }
        finally
        {
            DriverDiagnostics.FlushTelegram();
        }
    }

    private static string DriverDiagnosticsPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Extera Monitor", "Logs", "driver-startup.log");
}
