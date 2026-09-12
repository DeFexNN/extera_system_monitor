using Avalonia.Media;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class CpuWidgetStyleViewModel : ViewModelBase
{
    private Color _backgroundColor;
    private Color _textColor;
    private Color _mutedColor;
    private Color _borderColor;
    private Color _accentColor;

    public int Index { get; }
    public string StyleLabel => $"STYLE {Index}";

    public Color BackgroundColor { get => _backgroundColor; set => SetColor(ref _backgroundColor, value, nameof(BackgroundColor), nameof(BackgroundBrush)); }
    public Color TextColor { get => _textColor; set => SetColor(ref _textColor, value, nameof(TextColor), nameof(TextBrush)); }
    public Color MutedColor { get => _mutedColor; set => SetColor(ref _mutedColor, value, nameof(MutedColor), nameof(MutedBrush)); }
    public Color BorderColor { get => _borderColor; set => SetColor(ref _borderColor, value, nameof(BorderColor), nameof(BorderBrush)); }
    public Color AccentColor { get => _accentColor; set => SetColor(ref _accentColor, value, nameof(AccentColor), nameof(AccentBrush)); }

    public string BackgroundHex { get => ToHex(BackgroundColor); set => SetFromHex(value, color => BackgroundColor = color); }
    public string TextHex { get => ToHex(TextColor); set => SetFromHex(value, color => TextColor = color); }
    public string MutedHex { get => ToHex(MutedColor); set => SetFromHex(value, color => MutedColor = color); }
    public string BorderHex { get => ToHex(BorderColor); set => SetFromHex(value, color => BorderColor = color); }
    public string AccentHex { get => ToHex(AccentColor); set => SetFromHex(value, color => AccentColor = color); }

    public IBrush BackgroundBrush => new SolidColorBrush(BackgroundColor);
    public IBrush TextBrush => new SolidColorBrush(TextColor);
    public IBrush MutedBrush => new SolidColorBrush(MutedColor);
    public IBrush BorderBrush => new SolidColorBrush(BorderColor);
    public IBrush AccentBrush => new SolidColorBrush(AccentColor);
    public Action? Changed { get; set; }

    public CpuWidgetStyleViewModel(int index, string backgroundHex, string textHex, string mutedHex, string borderHex, string accentHex)
    {
        Index = index;
        _backgroundColor = ParseOrDefault(backgroundHex, Colors.Transparent);
        _textColor = ParseOrDefault(textHex, Colors.White);
        _mutedColor = ParseOrDefault(mutedHex, Colors.Gray);
        _borderColor = ParseOrDefault(borderHex, Colors.Gray);
        _accentColor = ParseOrDefault(accentHex, Colors.Teal);
    }

    public CpuWidgetStyleViewModel Clone(int index) => new(index, BackgroundHex, TextHex, MutedHex, BorderHex, AccentHex);

    public void Load(string backgroundHex, string textHex, string mutedHex, string borderHex, string accentHex)
    {
        SetInitial(ref _backgroundColor, backgroundHex, nameof(BackgroundColor), nameof(BackgroundBrush));
        SetInitial(ref _textColor, textHex, nameof(TextColor), nameof(TextBrush));
        SetInitial(ref _mutedColor, mutedHex, nameof(MutedColor), nameof(MutedBrush));
        SetInitial(ref _borderColor, borderHex, nameof(BorderColor), nameof(BorderBrush));
        SetInitial(ref _accentColor, accentHex, nameof(AccentColor), nameof(AccentBrush));
    }

    private void SetColor(ref Color field, Color value, string colorProperty, string brushProperty)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(colorProperty);
        OnPropertyChanged(brushProperty);
        OnPropertyChanged(GetHexProperty(colorProperty));
        Changed?.Invoke();
    }

    private void SetInitial(ref Color field, string hex, string colorProperty, string brushProperty)
    {
        var color = ParseOrDefault(hex, field);
        if (field == color) return;
        field = color;
        OnPropertyChanged(colorProperty);
        OnPropertyChanged(brushProperty);
        OnPropertyChanged(GetHexProperty(colorProperty));
    }

    private void SetFromHex(string value, Action<Color> setter)
    {
        if (TryParse(value, out var color)) setter(color);
    }

    private static string GetHexProperty(string colorProperty) => colorProperty switch
    {
        nameof(BackgroundColor) => nameof(BackgroundHex),
        nameof(TextColor) => nameof(TextHex),
        nameof(MutedColor) => nameof(MutedHex),
        nameof(BorderColor) => nameof(BorderHex),
        nameof(AccentColor) => nameof(AccentHex),
        _ => string.Empty
    };

    private static Color ParseOrDefault(string value, Color fallback) => TryParse(value, out var color) ? color : fallback;
    private static bool TryParse(string? value, out Color color)
    {
        try { color = Color.Parse(value ?? string.Empty); return true; }
        catch (FormatException) { color = default; return false; }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
