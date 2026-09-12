using System.Collections.ObjectModel;
using System.Windows.Input;
using ExteraMonitor.Models;
using ExteraMonitor.ViewModels;

namespace ExteraMonitor.ViewModels.Modules;

public sealed class CustomizationModuleViewModel : MonitorModuleViewModel
{
    private readonly CpuModuleViewModel _cpu;

    public ObservableCollection<CpuWidgetViewModel> Widgets => _cpu.Widgets;
    public ObservableCollection<AccentColorViewModel> Accents => _cpu.Accents;
    public ICommand ResetCommand { get; }

    public CustomizationModuleViewModel(CpuModuleViewModel cpu) : base("Customization")
    {
        _cpu = cpu;
        ResetCommand = new RelayCommand(_ => _cpu.ResetCustomization());
    }

    public override void Update(SystemSnapshot snapshot) { }

    public void Save() => _cpu.SaveCustomization();
}
