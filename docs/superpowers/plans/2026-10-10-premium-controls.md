# Premium Game Controls Implementation Plan

> Execute inline with writing-plans and executing-plans. This continues the already approved `2026-10-10-radiant-product-design.md`: game configuration adapters and CPU process controls. The user requested better paid functionality and selected the original Radiant Optimizer as the reference. No additional design approval round, commit or deployment is needed.

**Goal:** Ship 0.5.0 with saved, adjustable game profiles and reversible CPU controls for CS2, Fortnite and VALORANT.

**Architecture:** Extend the existing strict game transformation pipeline with versioned custom options; keep exact original bytes and existing recovery validation. Add an allowlisted Windows process adapter over the existing durable `OptimizationEngine`/`FileChangeJournal`, with PID plus creation time identity and process-exit reconciliation. Integrate all three recovery domains in the WPF recovery page.

**Tech Stack:** .NET 10, WPF, documented Win32 process APIs; existing TanStack website for download/release metadata.

## Evidence and scope

Inspected the original product's [official feature page](https://radiantoptimizer.com/utilities), [pricing page](https://radiantoptimizer.com/plans), [CPU control capture](https://radiantoptimizer.com/images/product-504/cpu-light-full.webp), and [game profile capture](https://radiantoptimizer.com/images/product-504/game-profiles-full.webp). Public UI establishes advertised controls, not measured speed gains or internal implementation.

Three directions considered: (1) editable game settings plus CPU controls, (2) saved performance comparisons, (3) NVIDIA/network/cleanup tuning. Choose (1): it adds actual user control within the approved design and reuses tested recovery. Comparisons are a future monitoring increment; driver/network controls need dedicated hardware coverage. Do not close background apps because reopening cannot recover unsaved work.

## Global constraints

- English UI; retain existing Free functionality and Premium activation. Recovery never checks licensing.
- Archive source before edits. Persist every original setting/file before mutation; refuse writes if backup fails.
- Preserve existing preset IDs and transformation behavior; old journals remain recoverable.
- Game settings only while game/required launcher is closed; unknown schemas and changed files block writes.
- Custom profile format is a versioned, bounded allowlist. No arbitrary config keys, paths or scripts.
- CPU actions target only known game executable instances in the current session. Validate creation time on a held native handle immediately before OS access; never act on a reused PID.
- UI offers Normal/Above normal priority only. Explicit logical-processor selection supports a single processor group; otherwise affinity stays unavailable. Do not label logical processors as physical or performance cores.
- Game exit ends its process settings naturally. Record that separately from a verified restoration; do not touch a replacement process. Access denied is not evidence of exit.
- Preserve unrelated payment work. No database, payment, license-server, deployment or git commit changes.

## Task 1 — Custom game profiles

Files: new `desktop/Radiant.Games/CustomGameProfiles.cs`, `SavedGameProfiles.cs`; extend `GameProfiles.cs`, `desktop/Radiant.Games.Tests/Program.cs`.

- [x] Add tests for chosen fields only, canonical versioned options, wrong-game fields/bounds, exact-byte restart restoration, changed-file protection, missing optional keys, save/load and corrupt profile rejection.
- [x] Introduce `CustomGameProfile` containing game and nullable approved option values (null = unchanged). `Encode`/`Decode` use a deterministic versioned identifier validated in both preview and recovery.
- [x] Reuse existing schema validation; apply only selected VSync/MSAA for CS2; VSync/frame cap and existing quality keys for Fortnite; frame cap/material/detail for VALORANT. Preserve resolution, bindings and every unselected key.
- [x] Save named profiles as immutable local files with generated identifiers, bounded names/counts and durable writes. Loading never writes a game config. Application still produces a fresh immutable preview.
- [x] Run `.tools/dotnet/dotnet.exe run --project desktop/Radiant.Games.Tests -c Release`; every generated fixture must restore exact original bytes.

## Task 2 — CPU process tuning

Files: new `desktop/Radiant.Windows/ProcessTuningModels.cs`, `NativeGameProcesses.cs`, `ProcessTuningService.cs`; new `desktop/Radiant.Processes.Tests/`; extend `scripts/test-desktop.mjs`.

- [x] Write fake-transport tests covering backup-before-write, backup failure, preview races, PID reuse, access denial, partial failure, restart recovery, ordered recovery, external conflicts and ended-process reconciliation.
- [x] `GameProcessTarget` records executable name, PID and native creation time. `ProcessTuningState` records priority, affinity and system mask. The adapter builds fixed `priority`/`affinity` targets and rechecks the inspected values before applying.
- [x] Native transport opens minimum-required rights, checks identity and current-session ownership, and holds the handle through each read/write. It accepts only CS2, Fortnite and VALORANT executable names. No elevation, injection, IFEO persistence, services or driver changes.
- [x] Reuse Core journal for immutable originals and conflict-aware ordered undo. Reconcile verified-exited instances under the journal lease without OS writes. Do not convert access errors to successful cleanup.
- [x] Native test changes only the test process through an internal test-only constructor; persists backups, reloads journal, restores exact priority/mask in `finally`. Normal app constructor cannot opt into arbitrary processes.
- [x] Run `.tools/dotnet/dotnet.exe run --project desktop/Radiant.Processes.Tests -c Release`.

## Task 3 — WPF integration and delivery

Files: new `desktop/Radiant.App/CustomGameProfilePage.cs`, `CpuControlPage.cs`; update MainWindow/GamePages/RecoveryPage, app version, build script, release metadata and website feature copy.

- [x] Custom editor defaults every field to unchanged; saved options load into editor. Preview is free; saving/applying custom profiles requires active Premium, rechecked immediately before mutation. Old built-in presets retain their rules.
- [x] CPU page lists only detected supported running game instances; inspection is free. Explain per-process lifetime, possible performance tradeoff, chosen logical CPUs and exact before/after values before paid apply.
- [x] Add CPU journals to single/bulk recovery with readable priority/mask labels. Ended processes show 'Process ended — no persistent change'; active conflicts remain visible.
- [x] Run full desktop suite and activation regressions, publish 0.5.0 with Premium activation enabled, capture own native pages, update accurate download/Premium copy.
- [x] Run TypeScript and `pnpm build`; verify release ZIP/SHA256 and browser download page. Report hardware/game limits and actual evidence without claiming FPS uplift.

API basis: [SetPriorityClass](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass), [processor groups](https://learn.microsoft.com/en-us/windows/win32/procthread/processor-groups), [SetProcessAffinityMask](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setprocessaffinitymask).
