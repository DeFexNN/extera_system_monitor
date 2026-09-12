using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ExteraMonitor.Views.Controls;

public sealed class CoreLoadGauge : Control
{
    public static readonly StyledProperty<double> UsageProperty =
        AvaloniaProperty.Register<CoreLoadGauge, double>(nameof(Usage));
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<CoreLoadGauge, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<CoreLoadGauge, IBrush?>(nameof(Track));

    public double Usage { get => GetValue(UsageProperty); set => SetValue(UsageProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UsageProperty || change.Property == AccentProperty || change.Property == TrackProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        if (diameter < 8) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var strokeWidth = Math.Clamp(diameter * 0.08, 2.5, 5);
        var radius = Math.Max(2, diameter / 2 - strokeWidth / 2 - 1);
        var trackPen = new Pen(Track ?? new SolidColorBrush(Color.FromArgb(110, 57, 91, 100)), strokeWidth);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        var progress = Math.Clamp(Usage / 100, 0, 1);
        var accentBrush = Accent ?? new SolidColorBrush(Color.Parse("#395B64"));
        var trackBrush = Track ?? new SolidColorBrush(Color.FromArgb(150, 57, 91, 100));
        var accentPen = new Pen(accentBrush, strokeWidth)
        {
            LineCap = PenLineCap.Round
        };

        if (progress >= 0.999)
        {
            context.DrawEllipse(null, accentPen, center, radius, radius);
        }
        else if (progress > 0)
        {
            var startAngle = -Math.PI / 2;
            var endAngle = startAngle + progress * Math.PI * 2;
            var start = new Point(center.X + Math.Cos(startAngle) * radius, center.Y + Math.Sin(startAngle) * radius);
            var end = new Point(center.X + Math.Cos(endAngle) * radius, center.Y + Math.Sin(endAngle) * radius);
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(start, false);
                path.ArcTo(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise);
            }
            context.DrawGeometry(null, accentPen, geometry);
        }

        // Five rounded mini-blocks visualize load in 20% steps, inside each ring.
        var blockSize = Math.Clamp(diameter * 0.085, 2, 6);
        var blockGap = Math.Max(1, blockSize * 0.35);
        var totalWidth = blockSize * 5 + blockGap * 4;
        var blockY = center.Y + radius * 0.48;
        var filledBlocks = (int)Math.Ceiling(progress * 5);
        for (var index = 0; index < 5; index++)
        {
            var x = center.X - totalWidth / 2 + index * (blockSize + blockGap);
            var fill = index < filledBlocks ? accentBrush : trackBrush;
            context.DrawRectangle(fill, null, new Rect(x, blockY, blockSize, Math.Max(1.5, blockSize * 0.65)), blockSize / 2, blockSize / 2);
        }
    }
}
