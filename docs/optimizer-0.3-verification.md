# Windows client 0.3.0 — implementation and verification

## Delivered behavior

The primary client now offers two Free, optional Windows adjustments: reducing client-area animations and disabling minimize/restore transitions. These affect desktop interaction; they do not promise a measured FPS improvement. Existing game preferences, Premium power-plan switching, game profiles, and monitoring retain their previous requirements.

Each apply operation saves typed originals before its first write, records write intent, checks for concurrent external changes, and verifies the result through the Windows API. A failed backup prevents the setting write. The interface shows current and proposed values and asks for confirmation before applying.

Recovery → **Restore all Windows changes** restores outstanding Windows operations newest first. It works without an account or Premium. Completed records are skipped, backups are retained, and external conflicts are not overwritten automatically. A conflict blocks older operations on the same setting while unrelated settings can recover. Individual conflict review authorizes restoration and first preserves the external value separately. Game-file backups remain separately recoverable on the same page.

Recovery files are local to `%LOCALAPPDATA%\OptimizerPreview\recovery`. A write-once, checksummed `.original` file retains initial values separately from mutable `.json` execution status. Lost status files can be reconciled with Windows from the original on the next history read. Corrupt backups stop changes. Legacy 0.2 journals remain readable. Keep this folder to retain recovery; the backups cover recorded settings, not a full Windows image.

## Source backups

Saved locally before editing:

- `artifacts/source-backups/client-before-optimization-20261010-142418.zip`
- `artifacts/source-backups/full-client-before-optimization-20261010-143635.zip`

The second archive contains the primary client before this change. Build output and local settings/keys are excluded. No commit or deployment is part of this work.

## Acceptance evidence

- **125 desktop checks passed:** Core 28, game profiles 41, monitoring 35, licensing 21. New cases cover newest-first order, repeat restoration without extra writes, conflicts, an unavailable setting, missing status files, corruption, original protection, and external-value preservation.
- **Native Windows roundtrip passed:** `artifacts/desktop/native-roundtrip-0.3.json`, captured October 10, 2026. Both animation preferences and an installed power plan were changed, their journal was reopened by a new engine, and all three settings were restored and verified against the originals. Three immutable original files remain.
- The native test exposed a valid active power scheme hidden from enumeration. Recovery now passes the saved GUID to `PowerSetActiveScheme`, which validates availability. New target selection still uses enumerated schemes. The initial test record was subsequently restored and verified; no test setting remains applied.
- Actual WPF renders of Overview, Windows settings, and Recovery are in `public/design-preview/radiant-client-*.png`. Recovery displays real restored test records. Capture is read-only and renders the app's own visual tree without opening a desktop window.
- TypeScript checking and the website production build passed. Release validation covers a Free build with activation explicitly unavailable.

## Reproduce

```powershell
$env:DOTNET_HOST_PATH=(Resolve-Path '.tools/dotnet/dotnet.exe').Path
pnpm desktop:test
pnpm desktop:build --free-preview
pnpm exec tsx --test tests/licenses.integration.test.ts
pnpm exec tsc --noEmit
pnpm build
```

Package: `public/downloads/optimizer-0.3.0-win-x64-preview.zip`. Extract and open `Optimizer.exe`. `public/downloads/optimizer-release.json` contains its size and SHA-256. An explicit package file list excludes local diagnostics.

The developer-only `--verify-settings-roundtrip <report.json> --data-directory <test-directory>` command intentionally changes both animation preferences and, when available, switches to another existing power plan. It creates a dedicated verification subfolder, backs up first, and restores in a finally path. Do not run it as an ordinary build check. `--snapshot <image.png> --snapshot-page recovery --data-directory <directory>` only reads and renders records.

## Release limits

This is an unsigned Windows x64 Free preview. The local build has no trusted activation keys, so Premium activation is unavailable. Free Windows adjustments and recovery remain usable. A configured production licensing release is still needed for Premium activation, along with code signing and broader Windows compatibility acceptance. No system restore point, full-disk backup, registry cleanup, driver tuning, or automatic updater was added.
