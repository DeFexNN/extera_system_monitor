using System.Windows.Input;
using Avalonia.Media;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class AccentColorViewModel : ViewModelBase
{
    private readonly Action<AccentColorViewModel> _changed;
    private Color _color;
    private string _hexText;

    public string Name { get; }
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
        }
    }
    public IBrush SwatchBrush => new SolidColorBrush(_color);
    public ICommand ApplyCommand { get; }

    public AccentColorViewModel(string name, string defaultHex, Action<AccentColorViewModel> changed)
    {
        Name = name;
        _color = Color.Parse(defaultHex);
        _hexText = defaultHex;
        _changed = changed;
        ApplyCommand = new RelayCommand(_ => Apply());
    }

    public Color GetColor() => _color;

    public void Load(string hex)
    {
        try
        {
            _color = Color.Parse(hex);
            HexText = hex;
            OnPropertyChanged(nameof(SelectedColor));
            OnPropertyChanged(nameof(SwatchBrush));
        }
        catch (FormatException) { }
    }

    public void Reset(string defaultHex)
    {
        _color = Color.Parse(defaultHex);
        HexText = defaultHex;
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(SwatchBrush));
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
            Commit();
        }
        catch (FormatException) { }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
