# Windows Read-only Client Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task by task in the current authorized workspace. Keep all existing website changes. User approved a runnable Windows first release with English UI, real hardware information and read-only checks on 2026-10-10. Execute inline; do not require another scope approval.

**Goal:** Deliver our own runnable Windows x64 application and replace every competitor screenshot in the homepage studies with captures rendered by our application.

**Architecture:** A standalone C# / .NET 10 WPF application under `desktop/preview/`. Windows collection is isolated from pure diagnostic rules and presentation. It reads hardware and system configuration at normal user privilege; it has no registry, power-plan or process mutation capability. A self-contained local test package is generated under ignored `artifacts/`.

**Tech Stack:** .NET 10, WPF, System.Management for WMI, documented read-only Windows APIs, existing TanStack Start website.

## Global constraints

- English only. Product name comes from one desktop build property, matching the website's configured app name.
- Real readings in the application. Unavailable readings remain unavailable; no random telemetry, fabricated scores, FPS promises, game presets or simulated optimization success.
- UI: original graphite sidebar, warm white workspace, restrained blue accent, hardware typography and a readable diagnostic ledger. Two implemented destinations: Overview and System checks. About describes the actual release scope.
- Every scan is read-only. No elevation, drivers, startup registration, configuration changes, process termination, telemetry or network upload.
- Do not include device IDs, serial numbers, usernames, IP/MAC addresses or file paths in website screenshots. Render a privacy-safe screenshot from the same UI and real nonsensitive hardware measurements.
- Installer, signing, automatic updates, paid activation and actual optimization are outside the user's approved first release. Provide a portable self-contained executable and clear local test status.
- Competitor image files, thumbnails containing them and outward product-source links must be removed from the delivered preview.
- Preserve the separate broader product-design draft. It is not an authorization to implement payments or modify Windows.

## Files and contracts

- `desktop/preview/Radiant.Core/Models.cs`: `HardwareSnapshot`, `DriveSnapshot`, `CheckResult`, and `CheckStatus`; nullable measurements preserve unknown states.
- `desktop/preview/Radiant.Core/Diagnostics.cs`: `IReadOnlyList<CheckResult> Evaluate(HardwareSnapshot snapshot)`; checks memory availability, system drive capacity, power mode and uptime. Results include evidence and a plain-language explanation. Unknown values are not passes.
- `desktop/preview/Radiant.Windows/HardwareCollector.cs`: `Task<HardwareSnapshot> CollectAsync(CancellationToken token)`; WMI queries have bounded timeout. Native power-scheme read uses `PowerGetActiveScheme`; registry is opened with `writable: false`. Failure in one category does not prevent other readings.
- `desktop/preview/Radiant.Desktop/MainWindow.xaml` plus view model: async refresh, clear busy/error/partial states, navigable Overview / System checks / About, optional local report export through a save dialog.
- `desktop/preview/Radiant.Desktop/App.xaml.cs`: normal launch and explicit developer `--verify` / `--render-preview` entry points. Headless rendering uses WPF layout and bitmap rendering, never a copied image.
- `desktop/preview/Radiant.Tests/Program.cs`: executable assertions for rule boundaries, unknown values and source-data errors; zero test-framework dependencies.
- `desktop/preview/build.ps1`: repeatable restore, tests and self-contained Windows x64 publish.
- `desktop/preview/README.md`: launch, build, data-read scope, limits and verification evidence.
- Website: `src/blocks/radiant/design-previews.tsx`, `messages/en.json`, `public/design-preview/`, and `docs/product-homepage-direction.md`.

## Task 1 — Core rules and actual Windows collection

- [x] Create projects and model contracts with `net10.0` for rules and `net10.0-windows` for Windows/WPF.
- [x] Add tests before rules: memory below 10% available is Attention, exactly 10% is not; missing total/free memory is Unknown; a missing system drive is Unknown; drive space below 15% is Attention; reported collection errors remain visible.

```csharp
Assert(Checks.ForMemory(totalBytes: 100, availableBytes: 9).Status == CheckStatus.Attention);
Assert(Checks.ForMemory(totalBytes: 100, availableBytes: 10).Status == CheckStatus.Pass);
Assert(Checks.ForMemory(totalBytes: null, availableBytes: null).Status == CheckStatus.Unknown);
Assert(Checks.ForStorage(totalBytes: 100, freeBytes: 14).Status == CheckStatus.Attention);
```

- [x] Implement deterministic rules with named thresholds, guard invalid values, and include units/evidence.
- [x] Implement real CPU/GPU/OS inventory, physical memory and fixed drive capacity. Exclude serials and network identifiers from the model. Include source notes and collection errors without full exception paths.
- [x] Run `dotnet run --project desktop/preview/Radiant.Tests` and the application verification mode; verify that this machine yields real CPU, memory and drive values, with unavailable values described honestly.

## Task 2 — Runnable original English client

- [x] Implement a WPF shell with original typography, custom visual styles and normal Windows window controls; bind every visible hardware value to the snapshot.
- [x] Refresh via `Task.Run` / async collection, disable duplicate refresh, use cancellation on window close, show last collected time, and report partial failures.
- [x] Show check status, evidence and explanation in a readable list. Never provide an Apply, Boost, Optimize, game preset or purchase action in this release.
- [x] Add About/release scope and explicit local JSON report export. Export includes only the modeled hardware readings and check results.
- [x] Add noninteractive verification and UI rendering entry points with nonzero exit on failures. Render Overview and Checks in light/dark using the same WPF views.
- [x] Publish self-contained Windows x64. Verify the published executable itself can collect and render on this Windows machine. Generate ZIP and SHA-256.

## Task 3 — Replace competitor material and deliver

- [x] Replace product images with the rendered original Overview and Checks. Update image tabs, alt text and captions to say this is our read-only preview build.
- [x] Remove the external competitor link and move the exact superseded competitor files and thumbnails out of `public/design-preview/` into ignored local artifacts.
- [x] Regenerate the three English homepage screenshots through the browser and check that none contains the old product.
- [x] Run `pnpm build`, `pnpm exec tsc --noEmit`, the core tests and published-executable verification. Inspect English desktop/preview/mobile layouts and image tabs.
- [x] Deliver the executable/ZIP path, English preview URL, concrete implemented capabilities, and the fact that this is a local unsigned first release. Update this plan with actual checks and outcomes.

## Review

The approved first release is covered by Tasks 1 and 2; removal of competitor assets is covered by Task 3. The broader payment and system-write plans are explicitly excluded. Missing sensor support must not be replaced by demo data. No database changes or website publication are required.

## Verification record — 2026-10-10

- Built with the project-local Microsoft .NET SDK 10.0.401 after checking the SDK archive against the vendor's SHA-512 release metadata.
- All 22 deterministic rule and report assertions passed. The published self-contained EXE successfully read this machine's CPU, memory, graphics and system drive; five checks ran. One unreadable Game Mode preference correctly remained Unknown.
- Four original WPF views were generated by the actual EXE with fresh hardware snapshots. Each command verified asynchronous refresh, navigation and rendered branding. The image render command is `--render-preview <directory> <overview|checks> <light|dark>`.
- Portable archive: `artifacts/windows/RadiantOptimizer-0.1.0-win-x64.zip` (about 58 MiB), containing the executable and README. The copy served by the local website has the same SHA-256. HTTP download verification returned a real ZIP signature (`504B0304`).
- The ten retired competitor images and old homepage thumbnails are preserved in ignored `artifacts/retired-preview-assets/`, outside the served website. All three English homepage thumbnails were recaptured with our own client.
- Website production build passed. The three H1s contain the full product name. Client image switching, enlargement and the download dialog were inspected. The 320 px layouts and 390 px download dialog fit without horizontal overflow; 768 px and desktop layouts were checked.
- No commit, deployment, payment activation or Windows settings mutation was performed. The binary is an unsigned local preview; cross-device certification and full tuning remain later work.
