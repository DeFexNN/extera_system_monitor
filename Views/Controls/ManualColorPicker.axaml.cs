using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ExteraMonitor.Views.Controls;

public partial class ManualColorPicker : UserControl
{
    public static readonly StyledProperty<Color> SelectedColorProperty = AvaloniaProperty.Register<ManualColorPicker, Color>(nameof(SelectedColor), Colors.Teal, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> SwatchSizeProperty = AvaloniaProperty.Register<ManualColorPicker, double>(nameof(SwatchSize), 32);
    public static readonly StyledProperty<IBrush> SwatchBrushProperty = AvaloniaProperty.Register<ManualColorPicker, IBrush>(nameof(SwatchBrush), new SolidColorBrush(Colors.Teal));

    private double _hue;
    private double _saturation;
    private double _value;
    private bool _draggingSpectrum;
    private bool _draggingHue;
    private bool _updating;

    public event EventHandler? ColorApplied;

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public double SwatchSize
    {
        get => GetValue(SwatchSizeProperty);
        set => SetValue(SwatchSizeProperty, value);
    }

    public IBrush SwatchBrush
    {
        get => GetValue(SwatchBrushProperty);
        private set => SetValue(SwatchBrushProperty, value);
    }

    public ManualColorPicker()
    {
        InitializeComponent();
        SwatchBrush = new SolidColorBrush(SelectedColor);
        SyncFromColor();
        UpdateEditor();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncFromColor();
        UpdateEditor();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedColorProperty && !_updating)
        {
            SwatchBrush = new SolidColorBrush(SelectedColor);
            SyncFromColor();
            UpdateEditor();
        }
    }

    private void SwatchButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncFromColor();
        UpdateEditor();
        PickerPopup.PlacementTarget = SwatchButton;
        PickerPopup.IsOpen = true;
    }

    private void Spectrum_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingSpectrum = true;
        e.Pointer.Capture(SpectrumBorder);
        UpdateSpectrum(e.GetPosition(SpectrumBorder));
        e.Handled = true;
    }

    private void Spectrum_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingSpectrum) UpdateSpectrum(e.GetPosition(SpectrumBorder));
    }

    private void Spectrum_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingSpectrum = false;
        e.Pointer.Capture(null);
    }

    private void Hue_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingHue = true;
        e.Pointer.Capture(HueBorder);
        UpdateHue(e.GetPosition(HueBorder));
        e.Handled = true;
    }

    private void Hue_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingHue) UpdateHue(e.GetPosition(HueBorder));
    }

    private void Hue_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingHue = false;
        e.Pointer.Capture(null);
    }

    private void Apply_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            SelectedColor = Color.Parse(HexBox.Text ?? string.Empty);
            SyncFromColor();
            UpdateEditor();
            PickerPopup.IsOpen = false;
            ColorApplied?.Invoke(this, EventArgs.Empty);
        }
        catch (FormatException)
        {
            UpdateEditor();
        }
    }

    private void UpdateSpectrum(Point point)
    {
        var width = Math.Max(1, SpectrumBorder.Bounds.Width);
        var height = Math.Max(1, SpectrumBorder.Bounds.Height);
        _saturation = Math.Clamp(point.X / width, 0, 1);
        _value = 1 - Math.Clamp(point.Y / height, 0, 1);
        SetColorFromHsv();
    }

    private void UpdateHue(Point point)
    {
        var width = Math.Max(1, HueBorder.Bounds.Width);
        _hue = Math.Clamp(point.X / width, 0, 1) * 360;
        SetColorFromHsv();
    }

    private void SetColorFromHsv()
    {
        SelectedColor = HsvColor.FromHsv(_hue, _saturation, _value).ToRgb();
        UpdateEditor();
    }

    private void SyncFromColor()
    {
        var hsv = new HsvColor(SelectedColor);
        _hue = hsv.H;
        _saturation = hsv.S;
        _value = hsv.V;
    }

    private void UpdateEditor()
    {
        if (!IsInitialized) return;
        _updating = true;
        var hueColor = HsvColor.FromHsv(_hue, 1, 1).ToRgb();
        SpectrumHueLayer.Background = new SolidColorBrush(hueColor);
        PreviewBorder.Background = new SolidColorBrush(SelectedColor);
        HexBox.Text = ToHex(SelectedColor);

        var spectrumWidth = Math.Max(1, SpectrumBorder.Bounds.Width);
        var spectrumHeight = Math.Max(1, SpectrumBorder.Bounds.Height);
        Canvas.SetLeft(SpectrumThumb, _saturation * spectrumWidth - SpectrumThumb.Width / 2);
        Canvas.SetTop(SpectrumThumb, (1 - _value) * spectrumHeight - SpectrumThumb.Height / 2);
        var hueWidth = Math.Max(1, HueBorder.Bounds.Width);
        Canvas.SetLeft(HueThumb, (_hue / 360) * hueWidth - HueThumb.Width / 2);
        _updating = false;
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
