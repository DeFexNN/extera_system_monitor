<p align="center">
  <img src="Documentation/extera-mark.svg" width="118" alt="Extera Monitor logo">
</p>

# 🖥️ Extera Monitor — Driver-Aware Windows Telemetry

An animated Windows system monitor that combines native OS counters, hardware sensor APIs, and a custom kernel telemetry channel in one polished Avalonia desktop interface.

Built with **C# / .NET 10**, **Avalonia UI**, **LibreHardwareMonitor**, **WMI**, **SQLite**, and the bundled `ExteraMonitorDriver` source.

> *"System telemetry should be accurate enough to trust and clear enough to understand at a glance."*

---

## Command Center

The overview keeps the important signals visible without turning the desktop into a wall of numbers. CPU load and temperature share one system-pulse card, GPU data comes from the hardware stack with an NVIDIA driver fallback, and disk activity is kept separate from occupied capacity.

![Extera Monitor dark command center](Documentation/screenshots/overview-dark.png)

<details>
<summary><b>Light theme</b></summary>

![Extera Monitor light command center](Documentation/screenshots/overview-light.png)

</details>

---

## Driver-Verified Startup

The application does not reveal the dashboard after an arbitrary delay. Its animated boot sequence waits for two consecutive complete samples and verifies:

- the `ExteraMonitorDriver` telemetry channel;
- CPU temperature plus GPU load and temperature sensors;
- CPU core, memory, storage, and process counters.

![Extera Monitor telemetry boot sequence](Documentation/screenshots/startup-sequence.png)

If a source is still initializing, the boot screen stays visible and reports the exact stage instead of presenting empty dashboard values.

---

## Highlights

- **Live CPU telemetry** — total and per-core load, package temperature, effective clocks, power, voltage, limits, and frequency distribution
- **GPU monitoring** — load and temperature through LibreHardwareMonitor with `nvidia-smi` fallback
- **Honest storage metrics** — capacity usage, active time, and current read/write throughput are displayed as separate measurements
- **Two complete themes** — a calm original light palette and a purpose-built dark palette
- **Animated interface** — crossfade/slide navigation, responsive card hover states, and an animated code-native Extera mark
- **Modular CPU workspace** — draggable, resizable, styled telemetry cards with persisted layouts
- **Local history** — lightweight SQLite persistence for samples, themes, accents, and widget configuration
- **Desktop overlay** — compact always-on-top telemetry for use outside the main window
- **Built-in visual capture mode** — deterministic UI screenshots for design verification

---

## Telemetry Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│                    Avalonia Presentation                    │
│  Command Center · CPU Workspace · Sensors · Overlay · Theme │
└───────────────────────────┬─────────────────────────────────┘
                            │ SystemSnapshot
┌───────────────────────────┴─────────────────────────────────┐
│                   Metrics Aggregation                       │
│  CPU cores · RAM · disks · network · processes · uptime     │
└───────────────┬───────────────────┬─────────────────────────┘
                │                   │
┌───────────────┴────────────┐  ┌───┴─────────────────────────┐
│ Windows Native Sources     │  │ Hardware Sources            │
│ NT counters · WMI · IP     │  │ Extera driver · LHM · NVAPI │
└────────────────────────────┘  └─────────────────────────────┘
                │                   │
                └──────────┬────────┘
                           ▼
                 SQLite history and settings
```

---

## Project Structure

```text
AvaloniaPanel/
├── Driver/                 # ExteraMonitorDriver C source and local runtime bundle
├── Models/                 # Immutable snapshots and saved configuration models
├── Services/               # Native counters, sensors, driver loader, SQLite history
├── ViewModels/             # Dashboard state and module logic
├── Views/
│   ├── Controls/           # Gauges, graphs, color picker, animated Extera logo
│   └── Modules/            # Overview, CPU, memory, storage, network, sensors…
├── App.axaml               # Semantic light/dark palette and shared motion styles
└── ExteraMonitor.csproj
```

---

## Build and Run

### Requirements

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Administrator access when loading the kernel driver
- A compatible `ExteraMonitorDriver.sys` runtime build for driver telemetry

```powershell
git clone https://github.com/DeFexNN/extera_system_monitor.git
cd extera_system_monitor
dotnet restore
dotnet run
```

The UI and standard Windows/LibreHardwareMonitor sources build without checked-in driver binaries. For the complete driver path, place the locally built `ExteraMonitorDriver.sys` and its loader runtime in `Driver/`; the project copies available runtime files into the output directory automatically.

### Release build

```powershell
dotnet publish ExteraMonitor.csproj -c Release -r win-x64 --self-contained false
```

---

## Visual Verification

The application includes a RenderTargetBitmap capture path used to verify the real running Avalonia UI:

```powershell
ExteraMonitor.exe --capture overview.png --capture-section Overview --capture-dark
ExteraMonitor.exe --capture startup.png --capture-section Overview --capture-dark --capture-loading
```

The startup capture waits for verified telemetry, while the regular capture waits until the boot overlay has completed.

---

## Technology

| Layer | Technology |
|---|---|
| Desktop UI | Avalonia 12, XAML, custom-drawn controls |
| Runtime | .NET 10 / C# |
| CPU and GPU sensors | ExteraMonitorDriver, LibreHardwareMonitor, NVIDIA SMI |
| Windows telemetry | NT system calls, WMI performance classes, .NET diagnostics |
| Persistence | Microsoft.Data.Sqlite |
| Motion | Avalonia page transitions, brush/transform transitions, 60 FPS logo control |
| CI | GitHub Actions on Windows |

---

## Scope

Extera Monitor is a Windows systems-programming and telemetry project. The driver source and low-level monitoring code are intended for development, hardware research, and controlled lab environments.

---

*"See the signal. Trust the source."*
