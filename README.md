# WDigger

WDigger is a console-based diagnostic and repair toolkit (TUI) for Windows,
built for advanced PC/laptop technicians. It is an **offline-first** alternative
to tools like WinUtil / Chris Titus WinUtil, with a deeper focus on hardware:
real-time sensors, full WMI inventory, SMART disk data and battery health.

Written in **C# on .NET 8** with **Spectre.Console** for the TUI.

> **Personal use tool.** Not intended for commercial distribution. Run as
> Administrator — sensors, WMI inventory, DISM, SFC, CHKDSK and most tweaks
> require elevation.

## Stack

| Layer | Technology |
|---|---|
| Language / runtime | C# 12, .NET 8 (Windows) |
| TUI | Spectre.Console (tables, menus, panels, progress bars, live views) |
| Hardware sensors | LibreHardwareMonitorLib (NuGet) |
| Inventory / SMART / battery / BIOS | System.Management (WMI) |
| Command orchestration | DISM, SFC, CHKDSK, netsh, PowerShell, Winget, pnputil, winfr, wevtutil, slmgr, ospp.vbs |
| Configuration | JSON files in `config/` (tweaks, software lists) |
| Logging | Timestamped audit log in `logs/` (stdout + stderr per command) |

## Folder structure

```
WDigger/
├── README.md
├── WDigger.sln
├── .gitignore
├── config/                 # JSON definitions (customizable, no recompile)
│   ├── tweaks.json
│   └── software.json
├── logs/                   # Created automatically; audit logs per session
└── src/WDigger/
    ├── WDigger.csproj
    ├── Program.cs          # Entry point + global crash handler
    ├── App.cs              # Application host composition
    ├── Core/
    │   ├── AppHost.cs      # Shared services container
    │   ├── CommandExecutor.cs   # Runs native commands, dry-run, logs output
    │   ├── CommandResult.cs
    │   ├── AppLogger.cs    # Timestamped file audit logger
    │   ├── ConfigManager.cs   # Loads config/*.json
    │   ├── RestorePointManager.cs   # WMI SystemRestore points
    │   └── IModule.cs
    ├── Models/
    │   ├── HardwareModels.cs
    │   ├── TweakDefinition.cs
    │   └── SoftwareDefinition.cs
    ├── Services/
    │   ├── SensorService.cs   # LibreHardwareMonitor sensors
    │   └── InventoryService.cs   # WMI inventory
    ├── UI/
    │   └── Shell.cs         # Main menu, navigation, settings
    └── Modules/
        ├── HardwareModule.cs     # Phase 2: inventory + live sensors
        ├── RepairModule.cs       # Phase 3: SFC, DISM, CHKDSK, WU repair
        ├── SoftwareModule.cs     # Phase 4: Winget, Appx removal
        ├── TweaksModule.cs       # Phase 4: JSON-driven tweaks + restore point
        ├── DriversModule.cs      # Phase 5: driver backup/restore
        ├── NetworkModule.cs      # Phase 5: network reset, DNS, connectivity
        └── DiskModule.cs         # Phase 5: disk space, CHKDSK, temp cleanup
```

## Build and run (Windows)

Requirements:

* Windows 10 / Windows 11
* [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer)
* Administrator rights (recommended — sensors and repair commands need elevation)

```cmd
git clone <repo-url> WDigger
cd WDigger\src\WDigger
dotnet restore
dotnet build
dotnet run
```

To start in dry-run mode (all modifying actions simulated, nothing changes):

```cmd
dotnet run -- --dry-run
```

To publish a standalone folder for a technician's USB stick:

```cmd
dotnet publish -c Release -r win-x64 --self-contained false
```

## Dry-run mode and safety

* Pass `--dry-run` on the command line or toggle it at runtime from
  **Settings > Enable/Disable dry-run mode**. In dry-run mode every action that
  would modify the system is only simulated: the exact command line (or
  registry change) is shown and logged, but never executed.
* Long-running repair operations (SFC, DISM, CHKDSK) render inside a live
  Spectre.Console progress bar so the terminal does not appear frozen, and each
  result is translated into a plain-language explanation after completion.
* Before applying any reversible tweak or major repair, WDigger attempts to
  create a **System Restore Point** via WMI (`SystemRestore.CreateRestorePoint`).
  Failure is reported but non-fatal — the operator decides whether to continue.
* Every applied tweak is recorded in `logs/applied-tweaks.json` so it can be
  rolled back later from **Tweaks > Roll back a previously applied tweak**.
* Every command's full stdout/stderr, exit code and duration are written to a
  timestamped log under `logs/WDigger_yyyyMMdd_HHmmss.log` for auditing.

## Configuration files

All tweak definitions and software lists live in `config/` and can be edited
without recompiling:

* `config/tweaks.json` — registry tweaks (`reg add` compatible) and native
  commands, each with an optional `rollback` list.
* `config/software.json` — default package lists for Winget installs.

See the files themselves for the exact schema; they are heavily commented by
example.

## Implemented phases

| Phase | Status | Content |
|---|---|---|
| 1 – Skeleton & UI | Done | .NET 8 project, Spectre.Console TUI, main menu, module navigation, settings, dry-run toggle |
| 2 – Hardware | Done | WMI inventory (OS, CPU, GPU, RAM, disks, motherboard, battery, network); real-time sensor monitor via LibreHardwareMonitor with live refreshing panel |
| 3 – Repair | Done (functional) | SFC /scannow, DISM RestoreHealth, CHKDSK /scan /perf, Windows Update component reset, Winget cache reset — all through CommandExecutor with logging, live progress bars for long operations and plain-language result explanations |
| 4 – Software & Tweaks | Done (functional) | Winget list/search/install (incl. unattended install from config/software.json), Appx removal, JSON-driven tweaks with registry/command actions, restore point before apply, applied-tweaks history and rollback |
| 5 – Advanced | Done (functional) | Driver backup (Export-WindowsDriver) with progress, pnputil enum/add-driver, network reset/DNS/IP + quick DNS config, connectivity test, disk space, CHKDSK scan with progress, temp cleanup |
| 6 – Refinement | Done | System health summary on main menu, expanded config/tweaks.json and config/software.json examples, improved error handling in hardware module |
| 7 – Advanced tools | Done | Devices (printers/PnP), Advanced Network (static IP/DHCP/firewall/proxy/traceroute/rename), Backup (.tibx Acronis), Activation (Windows/Office/HEU-KMS detect), File Recovery (winfr), Events & BIOS (event log, BIOS, motherboard, UEFI) |

## Extending WDigger

1. Create a class implementing `WDigger.Core.IModule` (`Name`, `Description`,
   `Run(AppHost host)`).
2. Instantiate it in `UI.Shell.Run()` and add a main-menu entry that calls
   `module.Run(host)`.
3. Use `host.Commands.Run(...)` for native commands (honors dry-run and logs
   output), `host.Sensors.ReadAll()` for live sensors,
   `host.Inventory.GetInventory()` for WMI inventory,
   `host.RestorePoints.Create(...)` for restore points, and
   `host.TweakHistory.Record(...)` / `.Remove(...)` to persist rollback state.
4. Add JSON entries to `config/tweaks.json` / `config/software.json` for
   data-driven tweaks and software lists.

## License

Personal use tool. No commercial distribution.
