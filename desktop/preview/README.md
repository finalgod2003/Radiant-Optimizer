# Windows desktop preview

This independent read-only client lives under `desktop/preview/`. The full optimizer client and its licensing, game profiles and monitoring remain in the sibling projects under `desktop/`.

An independently implemented English Windows client. Version 0.2.0 adds a complete hardware detail page, explained next steps, check filtering and readable offline reports to the read-only preview.

## Run the portable build

Extract the ZIP into a writable folder and open `RadiantOptimizer.exe`. The self-contained Windows x64 package includes the .NET runtime, so a separate .NET installation is not needed. A normal user account is sufficient. Closing the window exits the application; it does not install a service or a startup task.

This is an unsigned local preview, not a signed public release or installer. It has only been verified on the development machine; compatibility across Windows versions and hardware has not been certified. If Windows blocks execution, do not bypass its security protections; signed distribution is a separate release task.

## Implemented

- CPU model, core/logical processor counts, and a sampled CPU load reading.
- Physical memory visible to Windows, current availability, and system drive headroom.
- Graphics adapter names and driver versions, every fixed drive, Windows version and architecture, in a dedicated Hardware details page.
- Current Windows power plan, uptime, and the explicit user Game Mode preference when readable.
- Five explainable checks with Pass, Attention, Info or Unknown status. Unavailable data is not a pass. Power-plan choice and uptime alone are informational.
- All / To review / Unavailable check filters, with expandable explanations and status-specific next steps. No result is treated as a universal health score.
- Manual refresh (F5), cancel (Esc), and clear busy, empty and partial-data states. Canceling preserves the previous snapshot and prevents overlapping hardware scans.
- Original light/dark views (dark by default), Overview / Hardware / System checks / About navigation.
- Export a readable offline HTML report or structured JSON to your chosen folder. The HTML file contains no scripts or external resources, can be opened in a browser and printed, and escapes hardware-supplied text.

There is no system tuning, registry writing, game profile modification, driver installation, process termination, payment, license activation or automatic update in this release. The executable contains no telemetry or network upload path. Exported reports contain modeled hardware readings, not serial numbers, device IDs, hostnames, usernames or IP/MAC addresses.

## Interpretation

- Low available memory: less than 10% of Windows-visible physical memory at the sampled moment. This is a prompt to inspect workload, not a diagnosis of faulty RAM.
- Low system drive space: less than 15% available to the current user. It is a capacity finding, not a disk health test.
- CPU load is sampled across logical processors, not a benchmark or FPS measurement.
- Game Mode is read from the current user's explicit `AutoGameModeEnabled` value. A missing preference means Unknown; it does not mean disabled.
- No fixed performance improvement is promised.

## Build from source

Install a .NET 10 SDK for Windows x64, or use the project-local SDK at `.tools/dotnet`. From the repository root:

```powershell
./desktop/preview/build.ps1
```

The script runs the deterministic rule/report checks, publishes a self-contained executable, verifies live collection, renders the application's actual WPF views for the website, and creates a versioned ZIP and SHA-256 manifest under `artifacts/windows-preview/`. It derives the release version from `Directory.Build.props` and copies the ZIP and checksum to ignored `public/downloads/preview/`. The main website's download and pricing links use the full optimizer's existing routes. Use `-SkipWebAssets` to package this preview without updating the website. Local verification reports are kept outside the distributable ZIP.

Developer commands (no visible window, no system changes):

```powershell
./artifacts/windows-preview/RadiantOptimizer-win-x64/RadiantOptimizer.exe --verify ./artifacts/windows-preview/hardware-verification.json
./artifacts/windows-preview/RadiantOptimizer-win-x64/RadiantOptimizer.exe --render-preview ./public/design-preview overview light
```

`--verify` returns nonzero if CPU, memory or the system drive cannot be read on the test machine. It also writes `report-preview.html` and `report-preview.json` beside the verification result. Normal interactive use continues to show unavailable categories instead of failing the whole UI. Rendering accepts `overview` / `checks` / `hardware` and `light` / `dark`; the build script runs all six. Each view uses a fresh real hardware snapshot, verifies refresh/cancellation/filter/navigation state, and carries a read-only preview caption on the website.

## Implementation sources

Windows integration uses Microsoft's documented [WMI processor](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor), [operating system](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-operatingsystem), [memory status](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) and [power scheme](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powergetactivescheme) interfaces. UI and application code in this directory are original to this project.
