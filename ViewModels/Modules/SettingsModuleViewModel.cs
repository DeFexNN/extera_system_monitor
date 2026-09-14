using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ExteraMonitor.Models;
using ExteraMonitor.Services;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class SettingsModuleViewModel : MonitorModuleViewModel
{
    private IThemePaletteRepository? _repository;
    private string _selectedTheme = "Light";

    public ObservableCollection<ThemeColorViewModel> Colors { get; } = new();
    public IReadOnlyList<string> Themes { get; } = ["Light", "Dark"];
    public event Action<bool>? ThemeChanged;
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not ("Light" or "Dark") || _selectedTheme == value) return;
            _selectedTheme = value;
            OnPropertyChanged(nameof(SelectedTheme));
            ApplyThemePalette(value);
            ThemeChanged?.Invoke(value == "Dark");
            Save();
        }
    }
    public ICommand ResetCommand { get; }

    public SettingsModuleViewModel() : base("Settings")
    {
        Colors.Add(new ThemeColorViewModel("WINDOW", "ThemeWindowBrush", "Main application background and panels.", "#E7F6F2", ColorChanged));
        Colors.Add(new ThemeColorViewModel("CHROME", "ThemeChromeBrush", "Title bar, sidebar and dark surfaces.", "#2C3333", ColorChanged));
        Colors.Add(new ThemeColorViewModel("ACCENT", "ThemeAccentBrush", "Primary buttons, borders and telemetry lines.", "#395B64", ColorChanged));
        Colors.Add(new ThemeColorViewModel("SOFT ACCENT", "ThemeSoftBrush", "Secondary text, progress tracks and subtle highlights.", "#A5C9CA", ColorChanged));
        ResetCommand = new RelayCommand(_ => Reset());
    }

    public void Load(IThemePaletteRepository repository)
    {
        _repository = repository;
        var settings = repository.LoadThemePalette();
        _selectedTheme = settings.Theme == "Dark" ? "Dark" : "Light";
        ApplyThemePalette(_selectedTheme);
        ThemeChanged?.Invoke(_selectedTheme == "Dark");
        OnPropertyChanged(nameof(SelectedTheme));
        foreach (var color in Colors)
            if (settings.Colors.TryGetValue(color.Name, out var hex)) color.Load(hex);
    }

    public void Save()
    {
        _repository?.SaveThemePalette(new ThemePaletteSettings(Colors.ToDictionary(color => color.Name, color => color.HexText, StringComparer.OrdinalIgnoreCase), SelectedTheme));
    }

    public override void Update(SystemSnapshot snapshot) { }

    public void ApplyPreviewTheme(string theme)
    {
        _selectedTheme = theme == "Dark" ? "Dark" : "Light";
        ApplyThemePalette(_selectedTheme);
        ThemeChanged?.Invoke(_selectedTheme == "Dark");
        OnPropertyChanged(nameof(SelectedTheme));
    }

    private void ColorChanged(ThemeColorViewModel color) => Save();

    private void Reset()
    {
        ApplyThemePalette(SelectedTheme);
        ThemeChanged?.Invoke(SelectedTheme == "Dark");
        Save();
    }

    private void ApplyThemePalette(string theme)
    {
        var dark = theme == "Dark";
        Colors[0].Load(dark ? "#1B2426" : "#E7F6F2");
        Colors[1].Load(dark ? "#111819" : "#2C3333");
        Colors[2].Load(dark ? "#5B9294" : "#395B64");
        Colors[3].Load(dark ? "#314346" : "#A5C9CA");

        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        app.Resources["ThemeTextBrush"] = new SolidColorBrush(Color.Parse(dark ? "#E1ECE9" : "#2C3333"));
        app.Resources["ThemeMutedTextBrush"] = new SolidColorBrush(Color.Parse(dark ? "#A5BCB9" : "#395B64"));
        app.Resources["ThemeOnAccentBrush"] = new SolidColorBrush(Color.Parse(dark ? "#10191A" : "#E7F6F2"));
        app.Resources["ThemeChromeTextBrush"] = new SolidColorBrush(Color.Parse(dark ? "#E4EFEC" : "#E7F6F2"));
        app.Resources["ThemeChromeMutedBrush"] = new SolidColorBrush(Color.Parse(dark ? "#93ACAA" : "#8EAEAD"));
        app.Resources["ThemeCardBrush"] = new SolidColorBrush(Color.Parse(dark ? "#222E30" : "#F5FBF9"));
        app.Resources["ThemeBorderBrush"] = new SolidColorBrush(Color.Parse(dark ? "#466063" : "#8AB6B7"));
        app.Resources["ThemeChartGridBrush"] = new SolidColorBrush(Color.Parse(dark ? "#34474A" : "#D1E4E0"));
    }
}
