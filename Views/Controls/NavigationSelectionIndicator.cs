using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ExteraMonitor.Views.Controls;

/// <summary>Draws one selection pill and smoothly moves it between fixed-height navigation rows.</summary>
public sealed class NavigationSelectionIndicator : Control
{
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<NavigationSelectionIndicator, int>(nameof(SelectedIndex));
    public static readonly StyledProperty<double> ItemStrideProperty =
        AvaloniaProperty.Register<NavigationSelectionIndicator, double>(nameof(ItemStride), 58);
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<NavigationSelectionIndicator, IBrush?>(nameof(Accent));

    private readonly DispatcherTimer _timer;
    private double _currentY;
    private bool _attached;

    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public double ItemStride { get => GetValue(ItemStrideProperty); set => SetValue(ItemStrideProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public NavigationSelectionIndicator()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Animate());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _currentY = Math.Max(0, SelectedIndex) * ItemStride;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty || change.Property == ItemStrideProperty)
        {
            if (_attached) _timer.Start();
            InvalidateVisual();
        }
        else if (change.Property == AccentProperty) InvalidateVisual();
    }

    private void Animate()
    {
        var target = Math.Max(0, SelectedIndex) * ItemStride;
        var distance = target - _currentY;
        if (Math.Abs(distance) < 0.15)
        {
            _currentY = target;
            _timer.Stop();
        }
        else
        {
            // Exponential ease-out remains smooth even when the user changes tabs rapidly.
            _currentY += distance * 0.22;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0) return;
        var accent = Accent ?? new SolidColorBrush(Color.Parse("#70B8BA"));
        context.DrawRectangle(accent, null, new Rect(0, _currentY + 3, Bounds.Width, 52), 11, 11);
    }
}
