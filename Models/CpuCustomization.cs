namespace ExteraMonitor.Models;

public sealed record CpuWidgetLayout(string Kind, double X, double Y, double Width, double Height, int Style, string AccentColorHex);

public sealed record CpuWidgetStyleSettings(
    string Kind,
    int Style,
    string BackgroundHex,
    string TextHex,
    string MutedHex,
    string BorderHex,
    string AccentHex);

public sealed record CpuCustomizationSettings(
    IReadOnlyList<CpuWidgetLayout> Widgets,
    IReadOnlyDictionary<string, string> AccentColors,
    IReadOnlyList<CpuWidgetStyleSettings> WidgetStyles,
    string HistorySeries = "Load,Temp,Clock,Power",
    string HistoryRange = "10m",
    bool SensorsExpanded = false);
