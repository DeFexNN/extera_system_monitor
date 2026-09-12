using System.Collections.ObjectModel;
using System.Windows.Input;
using ExteraMonitor.Models;
using ExteraMonitor.Services;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class SettingsModuleViewModel : MonitorModuleViewModel
{
    private IThemePaletteRepository? _repository;

    public ObservableCollection<ThemeColorViewModel> Colors { get; } = new();
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
        foreach (var color in Colors)
            if (settings.Colors.TryGetValue(color.Name, out var hex)) color.Load(hex);
    }

    public void Save()
    {
        _repository?.SaveThemePalette(new ThemePaletteSettings(Colors.ToDictionary(color => color.Name, color => color.HexText, StringComparer.OrdinalIgnoreCase)));
    }

    public override void Update(SystemSnapshot snapshot) { }

    private void ColorChanged(ThemeColorViewModel color) => Save();

    private void Reset()
    {
        Colors[0].Reset("#E7F6F2");
        Colors[1].Reset("#2C3333");
        Colors[2].Reset("#395B64");
        Colors[3].Reset("#A5C9CA");
        Save();
    }
}
