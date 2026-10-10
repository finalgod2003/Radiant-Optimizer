# Windows client 0.4 — game performance and unified recovery

## Delivered scope

- **Gaming essentials (Free):** one reviewed, pre-backed-up operation sets current-user Game Mode preferences on and built-in game/background recording preferences off. Disabling recording removes Windows game-clip capture until restoration; other capture applications are unaffected.
- **Plugged-in performance (Free):** request Windows 11 Best Performance using `PowerGetUserConfiguredACPowerMode` and `PowerSetUserConfiguredACPowerMode`. Apply requires AC power and supported APIs. Battery policy is never written. The engine saves the previous AC GUID and verifies its restoration. This is a user preference, not a guarantee that firmware/thermal policy will select that effective mode.
- **Performance game presets (Free):** CS2 disables validated VSync/MSAA; Fortnite lowers existing shadow/effects/postprocess/foliage values and sets textures to Low; VALORANT lowers existing material/detail quality. Default presets preserve the current frame cap. Fortnite/VALORANT additionally offer explicit 60/90/120/144/165/240/360 ceilings. Lower quality can reduce rendering work; caps are ceilings, not predicted FPS.
- **Unified Recovery:** Restore all changes invokes both independent journals. Each restores newest first. An external conflict, unavailable setting, or running game blocks the affected record and older changes to the same file/setting; unrelated records continue. A failure in one journal does not suppress attempting the other. Original backups remain.

Game configuration writes require a reviewed diff, a session opt-in and a closed game. They preserve exact original bytes, encodings, resolution, input and unrelated fields. Legacy Competitive/Balanced targets and Premium gates remain unchanged. Unknown formats/values stay blocked. The UI leads with Game Boost and keeps desktop animation preferences collapsed.

## Backup locations

Client source before this change: `artifacts/source-backups/client-before-game-performance-20261010-150907.zip`.

Normal user data: `%LOCALAPPDATA%\OptimizerPreview\recovery` (typed Windows originals/status) and `%LOCALAPPDATA%\OptimizerPreview\game-profiles` (exact original/target file bytes and records). Recovery includes an Open backup folder action. Explicitly approved conflicting game restores additionally archive the external version before replacing it. These are setting/file backups, not system images.

## Verification

- **131 desktop regression checks passed:** Core 28, games 47, monitoring 35, licensing 21. Game coverage includes all Free presets, cap boundaries, unchanged default caps, exact-byte restoration, external races, backups before writing, newest-first bulk recovery, same-file conflict blocking, running-game isolation and restore after restarting the engine.
- **Actual Windows gaming roundtrip passed:** `artifacts/desktop/native-gaming-0.4.json`, captured October 10, 2026. Gaming essentials changed five backed-up preference values as needed; AC mode changed Balanced → Best Performance. A new engine reopened both original records and restored everything. Every original matched, including previously absent registry values. No operation was skipped and no test setting remains applied.
- The three games were tested using generated configuration fixtures with their recognized schemas. This verifies file edits and recovery, **not real-game launch compatibility or measured FPS gains**. Check the in-game settings after applying on a supported installation.
- Original WPF renders are generated from the primary 0.4 client. Game Boost uses actual Windows readings; Recovery shows the restored native acceptance records. Rendering never applies changes.
- Desktop publish, TypeScript checking and the website production build passed. The final ZIP has the six intended release files and matches the manifest: 59,408,943 bytes, SHA-256 `6f681c4e0421563c63c618903369514e114fde4b26c205d5c454dd7786c6bbea`. Its local download endpoint returned HTTP 200. The English homepage, Game Boost preview dialog and download page were checked in the browser; the visible version, checksum and Free activation status match the package.

## Build and release

```powershell
$env:DOTNET_HOST_PATH=(Resolve-Path '.tools/dotnet/dotnet.exe').Path
pnpm desktop:test
pnpm desktop:build --free-preview
pnpm exec tsc --noEmit
pnpm build
```

Package: `public/downloads/optimizer-0.4.0-win-x64-preview.zip`. Extract it and run `Optimizer.exe`; no separate SDK is needed. The generated `public/downloads/optimizer-release.json` supplies its current size/SHA-256. Older ZIPs remain available locally.

This local unsigned Free build has no Premium activation keys. Its Free Game Boost, Performance presets and recovery work without activation. Broader Windows/game-version acceptance, code signing and a configured licensing release remain required for a production release.

Developer-only native acceptance: `--verify-gaming-roundtrip <report.json> --data-directory <test-root>`. It intentionally changes Windows settings, creates a dedicated journal folder, and restores in a finally path. Normal build, diagnostics and `--snapshot` rendering are read-only.

## API and setting references

- [Windows AC mode getter](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode) and [setter](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetuserconfiguredacpowermode): the requested mode can be overridden by other system signals.
- Existing supported game-format boundaries remain those documented in `docs/game-profile-support.md`; presets modify recognized keys only and preserve legacy targets for old recovery records.
