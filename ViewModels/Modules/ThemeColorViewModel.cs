using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class ThemeColorViewModel : ViewModelBase
{
    private readonly Action<ThemeColorViewModel> _changed;
    private Color _color;
    private string _hexText;

    public string Name { get; }
    public string ResourceKey { get; }
    public string Description { get; }
    public string HexText { get => _hexText; set => SetProperty(ref _hexText, value); }
    public Color SelectedColor
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            HexText = ToHex(value);
            OnPropertyChanged(nameof(SwatchBrush));
            ApplyResource(value);
        }
    }
    public IBrush SwatchBrush => new SolidColorBrush(_color);
    public ICommand ApplyCommand { get; }

    public ThemeColorViewModel(string name, string resourceKey, string description, string defaultHex, Action<ThemeColorViewModel> changed)
    {
        Name = name;
        ResourceKey = resourceKey;
        Description = description;
        _color = Color.Parse(defaultHex);
        _hexText = defaultHex;
        _changed = changed;
        ApplyCommand = new RelayCommand(_ => Apply());
        ApplyResource(_color);
    }

    public void Load(string hex)
    {
        try
        {
            _color = Color.Parse(hex);
            HexText = ToHex(_color);
            OnPropertyChanged(nameof(SelectedColor));
            OnPropertyChanged(nameof(SwatchBrush));
            ApplyResource(_color);
        }
        catch (FormatException) { }
    }

    public void Reset(string defaultHex)
    {
        _color = Color.Parse(defaultHex);
        HexText = defaultHex;
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(SwatchBrush));
        ApplyResource(_color);
    }

    public void Commit() => _changed(this);

    private void Apply()
    {
        try
        {
            _color = Color.Parse(HexText);
            HexText = ToHex(_color);
            OnPropertyChanged(nameof(SelectedColor));
            OnPropertyChanged(nameof(SwatchBrush));
            ApplyResource(_color);
            Commit();
        }
        catch (FormatException) { }
    }

    private void ApplyResource(Color color)
    {
        if (Application.Current is not { } app) return;
        app.Resources[ResourceKey] = new SolidColorBrush(color);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
