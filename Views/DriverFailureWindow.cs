using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ExteraMonitor.Services;

namespace ExteraMonitor.Views;

internal sealed class DriverFailureWindow : Window
{
    public DriverFailureWindow()
    {
        var hvciBlocked = KernelDriverLoader.LastFailureWasHvciBlocked;
        Title = hvciBlocked ? "Extera Monitor — HVCI blocks driver loading" : "Extera Monitor — driver startup failed";
        Width = 820;
        Height = 660;
        MinWidth = 620;
        MinHeight = 480;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#111819");

        var root = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions
            {
                new(GridLength.Auto),
                new(new GridLength(1, GridUnitType.Star)),
                new(GridLength.Auto)
            },
            RowSpacing = 18
        };

        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock
        {
            Text = hvciBlocked ? "HVCI / Memory Integrity is active" : "Driver could not start",
            FontSize = 25,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#F4F7F7")
        });
        heading.Children.Add(new TextBlock
        {
            Text = hvciBlocked
                ? "Driver loading was skipped; KVC was not started. Disable HVCI / Memory Integrity and restart the PC before launching Extera Monitor again."
                : "The monitor opened, but Windows did not start its kernel driver. Share this report with the developer; the same details are saved in the logs folder.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#BAC7C7")
        });
        root.Children.Add(heading);

        var report = new TextBox
        {
            Text = KernelDriverLoader.LastFailureReport ?? $"Driver failed to start.{Environment.NewLine}Log: {DriverDiagnostics.LogFilePath}",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(14),
            Background = Brush("#182223"),
            Foreground = Brush("#D9E4E4"),
            BorderBrush = Brush("#38575A")
        };
        Grid.SetRow(report, 1);
        root.Children.Add(report);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        var openLogs = new Button
        {
            Content = "Open logs folder",
            Padding = new Thickness(16, 9),
            Background = Brush("#263638"),
            Foreground = Brush("#E3EEEE")
        };
        openLogs.Click += OpenLogsFolder;
        actions.Children.Add(openLogs);

        var close = new Button
        {
            Content = "Close Extera Monitor",
            Padding = new Thickness(16, 9),
            Background = Brush("#4B8587"),
            Foreground = Brush("#071314")
        };
        close.Click += (_, _) => Close();
        actions.Children.Add(close);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Content = root;
    }

    private void OpenLogsFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var path = Path.GetDirectoryName(DriverDiagnostics.LogFilePath)!;
            Directory.CreateDirectory(path);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            DriverDiagnostics.Write("driver.failure.open-logs.failed", $"Could not open diagnostics folder: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
