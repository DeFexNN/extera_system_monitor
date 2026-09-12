using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ExteraMonitor.Views.Controls;

public sealed class TelemetryGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<TelemetryGraph, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<TelemetryGraph, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<TelemetryGraph, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<TelemetryGraph, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<bool> ShowGridProperty = AvaloniaProperty.Register<TelemetryGraph, bool>(nameof(ShowGrid), true);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty || change.Property == StrokeProperty || change.Property == MinimumProperty || change.Property == MaximumProperty || change.Property == ShowGridProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 2 || height <= 2) return;

        if (ShowGrid)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 57, 91, 100)), 1);
            for (var row = 1; row < 4; row++)
            {
                var y = height * row / 4;
                context.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            }
        }

        var values = Values;
        if (values is null || values.Count < 2) return;
        var min = Minimum;
        var max = Maximum <= min ? min + 1 : Maximum;
        var pen = new Pen(Stroke ?? new SolidColorBrush(Color.Parse("#395B64")), 2.5);
        var validValues = values.Where(double.IsFinite).ToArray();
        if (validValues.Length < 2) return;
        for (var index = 1; index < validValues.Length; index++)
        {
            var x1 = (index - 1) * width / (validValues.Length - 1);
            var x2 = index * width / (validValues.Length - 1);
            var y1 = height - Math.Clamp((validValues[index - 1] - min) / (max - min), 0, 1) * height;
            var y2 = height - Math.Clamp((validValues[index] - min) / (max - min), 0, 1) * height;
            context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }
    }
}
