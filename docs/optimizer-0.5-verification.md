# 0.5.0 Premium controls — verification

Date: 2026-10-10. This continues the approved product design and adds independently implemented controls informed by the original product's [public feature page](https://radiantoptimizer.com/utilities) and its public CPU/game-profile screenshots. The competitor executable was not installed or run. Public descriptions are not benchmark evidence.

## Delivered

- **Premium custom game profiles:** select supported fields, save a named local setup, load it and generate a fresh diff before applying. CS2 exposes VSync/MSAA; Fortnite exposes VSync/FPS cap and existing shadow/texture/effects/postprocess/foliage fields; VALORANT exposes its always-on cap and existing material/detail fields. Unselected fields, resolution and inputs remain unchanged.
- **Premium CPU control:** inspect a supported running game, choose Normal/Above normal priority and optionally select logical processors. Exact native process creation time plus PID identifies the instance. The production transport only accepts the three supported game executable names in the current session. Single-processor-group support is required for affinity; protected-process access may be denied.
- **Recovery:** durable originals precede CPU writes; existing Core journaling handles interrupted operations, external changes, immutable backups and ordered undo. Free single/bulk recovery now covers Windows, game files and CPU settings. Confirmed process exit is displayed as “Process ended — no persistent change”, not as a verified write-back. Access failure is not treated as exit.
- Basic Free settings, game presets, monitoring and recovery retain their access rules. Premium activation remains enabled. Saving/applying custom profiles and applying CPU changes check the signed cached entitlement; inspection and recovery do not.

## Evidence

- Desktop suites: Core **28/28**, Games **51/51**, Monitoring **35/35**, Licensing **21/21**; new CPU suite **11/11**. Total **146** checks. The final CPU pass includes the added race between inspection and initial backup, and rejection of the test process by the normal production allowlist.
- Key/bootstrap regressions: **3/3**.
- Native CPU acceptance modified **only its own test process**. It persisted originals, changed priority/affinity, reloaded the service from disk, restored both originals and verified equality; `finally` retries recovery if needed. Final native originals remain at `C:\Users\hp\AppData\Local\Temp\radiant-cpu-native-cea5e8a7f3e94ddfab1b709622ff8b36`.
- Game writes used generated fixtures only. Tests confirm exact-byte restore after restarting the engine, selected-field isolation, rejected bounds/wrong-game fields, immutable versioned options, saved-profile restart/corruption and stale-preview rejection.
- .NET WPF build and self-contained Windows x64 publish passed. TypeScript and `pnpm build` passed. Current build includes the workspace's concurrent brand assets without overwriting payment or branding work.
- Browser `/pricing` shows custom game profiles and running-game CPU controls in the Premium list; Free recovery remains listed. `/download` advertises the new scope and activation instructions.

Logs: `artifacts/desktop/premium-controls-tests.log`, `cpu-controls-final-tests.log`, `game-custom-tests.log`, `key-bootstrap-0.5-tests.log`, `build-0.5.log`, `typecheck-0.5.log`, `web-build-0.5.log`, `premium-0.5-package-evidence.json`.

## Release

- `public/downloads/optimizer-0.5.0-win-x64-preview.zip`
- Size: **59,473,701 bytes**
- SHA-256: `8c8f086137ac53fb5f3eb4577467ff7f141ee4bbed3472aeb4cf078f641cedc3`
- Six explicitly allowlisted release files; activation is enabled. The configured license server remains `http://localhost:3000`, so this is a local development preview.
- Source backup: `artifacts/source-backups/client-before-premium-controls-20261010-160751.zip`.

## Limits

No live CS2, Fortnite or VALORANT installation was available for end-to-end menu or anti-cheat compatibility checks. No FPS improvement is claimed. CPU selection can reduce performance and expires with the process; it is not a persistent launch rule or a core-parking feature. Windows offers no compare-and-swap for these process properties: reads around writes detect supported conflicts, but another program can change a value after verification.

NVIDIA driver tuning, application closing, new game formats and automatic gaming sessions are not included. The local website currently reports checkout unavailable until its payment provider is configured; this update does not change billing settings or grant a Premium license.

Native images in `public/design-preview/radiant-client-*.png` are actual renders from our WPF app, using real detection. Empty game lists are intentionally shown when no supported game is running or configured.
