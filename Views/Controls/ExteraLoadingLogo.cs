using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ExteraMonitor.Views.Controls;

/// <summary>A lightweight, code-native telemetry spinner used while hardware sources initialize.</summary>
public sealed class ExteraLoadingLogo : Control
{
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<ExteraLoadingLogo, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ExteraLoadingLogo, IBrush?>(nameof(Track));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public ExteraLoadingLogo()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            _phase = (_phase + 0.012) % 1;
            InvalidateVisual();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsVisible) _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccentProperty || change.Property == TrackProperty) InvalidateVisual();
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible) _timer.Start();
            else _timer.Stop();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 24) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var accent = Accent ?? new SolidColorBrush(Color.Parse("#6EB8BA"));
        var track = Track ?? new SolidColorBrush(Color.FromArgb(90, 147, 172, 170));
        var pulse = 0.93 + Math.Sin(_phase * Math.PI * 2) * 0.035;

        context.DrawEllipse(null, new Pen(track, 1.2), center, size * 0.46, size * 0.46);
        context.DrawEllipse(null, new Pen(track, 1), center, size * 0.34, size * 0.34);

        DrawArc(context, center, size * 0.46, _phase * 360 - 90, 82, new Pen(accent, 4) { LineCap = PenLineCap.Round });
        DrawArc(context, center, size * 0.34, -_phase * 420 + 35, 118, new Pen(accent, 2.2) { LineCap = PenLineCap.Round });

        for (var index = 0; index < 8; index++)
        {
            var angle = (_phase * 360 + index * 45) * Math.PI / 180;
            var radius = size * 0.405;
            var dot = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            var opacity = 0.18 + index / 9d;
            var color = accent is ISolidColorBrush solid
                ? new SolidColorBrush(solid.Color, opacity)
                : accent;
            context.DrawEllipse(color, null, dot, 1.5 + index * 0.12, 1.5 + index * 0.12);
        }

        var half = size * 0.105 * pulse;
        var logoPen = new Pen(accent, Math.Max(3, size * 0.035)) { LineCap = PenLineCap.Round };
        context.DrawLine(logoPen, new Point(center.X - half, center.Y - half), new Point(center.X - half, center.Y + half));
        context.DrawLine(logoPen, new Point(center.X - half, center.Y - half), new Point(center.X + half, center.Y - half));
        context.DrawLine(logoPen, new Point(center.X - half, center.Y), new Point(center.X + half * 0.65, center.Y));
        context.DrawLine(logoPen, new Point(center.X - half, center.Y + half), new Point(center.X + half, center.Y + half));
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double startDegrees, double sweepDegrees, Pen pen)
    {
        var startRadians = startDegrees * Math.PI / 180;
        var endRadians = (startDegrees + sweepDegrees) * Math.PI / 180;
        var start = new Point(center.X + Math.Cos(startRadians) * radius, center.Y + Math.Sin(startRadians) * radius);
        var end = new Point(center.X + Math.Cos(endRadians) * radius, center.Y + Math.Sin(endRadians) * radius);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
