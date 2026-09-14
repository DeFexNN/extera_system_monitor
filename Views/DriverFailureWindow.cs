using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ExteraMonitor.Services;

namespace ExteraMonitor.Views;

internal sealed class DriverFailureWindow : Window
{
    private readonly TextBlock? _hvciActionStatus;
    private readonly Button? _restartComputer;

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

        if (hvciBlocked)
        {
            var prompt = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Background = Brush("#1A292A")
            };
            prompt.Children.Add(new TextBlock
            {
                Text = "Do you want to turn off Memory Integrity and restart this PC?",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#F4F7F7"),
                TextWrapping = TextWrapping.Wrap
            });
            prompt.Children.Add(new TextBlock
            {
                Text = "Windows requires you to change this setting yourself. Choose Yes to open Windows Security, turn off Memory integrity under Device security → Core isolation, then return here to restart.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#BAC7C7")
            });

            _hvciActionStatus = new TextBlock
            {
                IsVisible = false,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#7FC1B8")
            };
            prompt.Children.Add(_hvciActionStatus);

            var choiceButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            var yes = new Button
            {
                Content = "Yes — open Windows Security",
                Padding = new Thickness(14, 8),
                Background = Brush("#4B8587"),
                Foreground = Brush("#071314")
            };
            yes.Click += OpenMemoryIntegritySettings;
            choiceButtons.Children.Add(yes);

            _restartComputer = new Button
            {
                Content = "Restart PC",
                Padding = new Thickness(14, 8),
                Background = Brush("#263638"),
                Foreground = Brush("#E3EEEE"),
                IsVisible = false
            };
            _restartComputer.Click += RestartComputer;
            choiceButtons.Children.Add(_restartComputer);

            var no = new Button
            {
                Content = "No — leave settings unchanged",
                Padding = new Thickness(14, 8),
                Background = Brush("#263638"),
                Foreground = Brush("#E3EEEE")
            };
            no.Click += (_, _) => Close();
            choiceButtons.Children.Add(no);
            prompt.Children.Add(choiceButtons);

            var promptBorder = new Border
            {
                Background = Brush("#1A292A"),
                BorderBrush = Brush("#4B8587"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Child = prompt
            };
            Grid.SetRow(promptBorder, 1);
            root.Children.Add(promptBorder);
        }

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
        Grid.SetRow(report, 2);
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

        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        Content = root;
    }

    private void OpenMemoryIntegritySettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            // This opens the documented Windows Security page. The user changes the
            // protection setting there, rather than this app silently editing policy.
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                ArgumentList = { "ms-settings:windowsdefender" }
            });
            DriverDiagnostics.Write("driver.hvci.settings-opened", "Opened Windows Security at the user's request.");
            if (_hvciActionStatus is not null)
            {
                _hvciActionStatus.Text = "In Windows Security, open Device security → Core isolation details and turn Memory integrity off. When you are ready, return here and choose Restart PC.";
                _hvciActionStatus.IsVisible = true;
            }
            if (_restartComputer is not null) _restartComputer.IsVisible = true;
        }
        catch (Exception ex)
        {
            DriverDiagnostics.Write("driver.hvci.settings-open.failed", $"Could not open Windows Security: {ex.GetType().Name}: {ex.Message}");
            if (_hvciActionStatus is not null)
            {
                _hvciActionStatus.Text = "Windows Security did not open. Go to Start → Windows Security → Device security → Core isolation details, turn Memory integrity off, then return here to restart.";
                _hvciActionStatus.IsVisible = true;
            }
            if (_restartComputer is not null) _restartComputer.IsVisible = true;
        }
    }

    private void RestartComputer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            // Keep a grace period so the user can save open work and cancel if needed.
            var startInfo = new ProcessStartInfo("shutdown.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("/r");
            startInfo.ArgumentList.Add("/t");
            startInfo.ArgumentList.Add("60");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("Extera Monitor: restart requested after changing Memory Integrity. Save your work; run shutdown /a to cancel.");
            Process.Start(startInfo);
            DriverDiagnostics.Write("driver.hvci.restart-requested", "User requested a system restart with a 60-second cancellation window after the HVCI prompt.");
            if (_hvciActionStatus is not null)
            {
                _hvciActionStatus.Text = "Restart scheduled in 60 seconds. Save your work now. To cancel, run shutdown /a.";
                _hvciActionStatus.IsVisible = true;
            }
            _restartComputer!.IsEnabled = false;
        }
        catch (Exception ex)
        {
            DriverDiagnostics.Write("driver.hvci.restart.failed", $"Could not request system restart: {ex.GetType().Name}: {ex.Message}");
            if (_hvciActionStatus is not null)
            {
                _hvciActionStatus.Text = $"Could not start the restart: {ex.Message}. You can restart Windows manually after turning Memory integrity off.";
                _hvciActionStatus.IsVisible = true;
            }
        }
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
