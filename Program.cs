using Avalonia;
using System;

namespace ExteraMonitor;

sealed class Program
{
    internal static string? CpuCapturePath { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var captureIndex = Array.IndexOf(args, "--capture-cpu");
        if (captureIndex >= 0)
        {
            var outputPath = captureIndex + 1 < args.Length && !args[captureIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[captureIndex + 1]
                : "cpu-proof.png";
            CpuCapturePath = Path.GetFullPath(outputPath);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(CpuCapturePath is null ? args : []);
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
