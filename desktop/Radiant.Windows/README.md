# Windows engine

`WindowsOptimizer` exposes the public methods consumed by the WPF app. It has no
network, account, payment, or licensing dependency. Restoring a saved change is
always available regardless of entitlement state.

## Implemented operations

Registry paths below are under **HKEY_CURRENT_USER**. The adapter maps fixed IDs
to paths or native APIs itself; callers cannot supply a registry path or script.

| Action ID            | Title              | Setting IDs / fixed values                                                                                                                               | Applied target                                |
| -------------------- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `game-mode`          | Game Mode          | `gaming.auto-game-mode`: `Software\Microsoft\GameBar\AutoGameModeEnabled`; `gaming.allow-auto-game-mode`: same key, `AllowAutoGameMode`                  | DWORD 1 / 1                                   |
| `game-recording`     | Game recording     | `gaming.game-dvr`: `System\GameConfigStore\GameDVR_Enabled`; `gaming.app-capture`: `Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` | DWORD 0 / 0                                   |
| `background-capture` | Background capture | `gaming.historical-capture`: `Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled`                                                | DWORD 0                                       |
| `power-plan`         | Power plan         | `power.active-plan`, Windows PowrProf API                                                                                                                | Existing enumerated GUID provided as `option` |

Two additional Free actions use documented Windows animation APIs:

- `client-animations`: `interface.client-animations`, GET/SETCLIENTAREAANIMATION.
- `window-animations`: `interface.window-animations`, GET/SETANIMATION.

Both target Disabled, retain the saved Boolean, and require per-action review.
They persist with `SPIF_UPDATEINIFILE` and send a bounded `WM_SETTINGCHANGE`
notification. They do not require the experimental gaming-session opt-in.
These are desktop preferences, not measured FPS boosts.

Version 0.4 adds two Free gaming actions:

- `gaming-ready`: the five allowlisted Game Mode/recording values above, backed
  up together before the first write. Targets are Game Mode on and capture off.
- `gaming-power`: `power.ac-performance-mode`, via the documented Windows 11
  `PowerGetUserConfiguredACPowerMode` / `PowerSetUserConfiguredACPowerMode` APIs.
  Requests Best Performance only after checking AC is connected. Recovery can
  restore the saved AC GUID even on battery. Battery policy is never written.
  Windows firmware/thermal signals may override the effective mode.

Gaming actions are exposed only on Windows build 19041 or later. An unexpected
existing registry type disables new applications, and a machine Game DVR policy
disables recording actions. This is a runtime capability check, **not a completed
Windows compatibility certification**. Native gaming writes/recovery passed on
the development PC; game performance effects still require per-game measurement.
Each Windows adjustment needs review and confirmation. No settings are
automatically applied on startup or scan. Game-file writes additionally retain
their session opt-in.

All registry changes use the current user. No process elevation, arbitrary
commands, HKLM writes, power-plan creation, individual power-setting tuning,
process termination, service changes, or driver changes are implemented.

Power operations use `PowerEnumerate`, `PowerGetActiveScheme`,
`PowerReadFriendlyName`, and `PowerSetActiveScheme`; they do not shell out to
`powercfg`. New targets must be enumerated plans. Restore uses the exact saved
GUID because Windows can hide a valid active scheme from enumeration.
`PowerSetActiveScheme` validates availability; removed plans are never created.
The active GUID is read back. Permission or policy failures are returned explicitly.

## Recovery semantics

- The engine takes a typed snapshot of every value before writing anything.
- The per-user `dataDirectory/recovery` journal is flushed to disk before the
  first mutation, and records intent before each subsequent write. Entries are
  replaced from a flushed temporary file in the same directory.
- Version 0.3 retains write-once `.original` files with SHA-256 checksums. Status
  updates cannot alter the original/target values. Missing status files recover
  from the original; damaged originals stop changes. Old 0.2 records remain readable.
- A cross-process lock and an in-process gate prevent concurrent optimizer
  writers using the same journal. Failed/corrupt journal reads stop new changes.
- Each write is preceded by a current-value conflict check after journal
  persistence, followed by a readback. A final readback verifies the whole action.
- Values store absence separately from DWORD zero, retain their original
  registry type, preserve unexpanded strings, and serialize byte/string arrays
  losslessly. Restore deletes a value that originally did not exist; an empty
  parent registry key created during apply may remain.
- Ordinary restore refuses values that match neither the original nor the target.
  Explicit `force: true` authorizes the current observed values. Even force does
  not suppress the later pre-write conflict check. There is no cross-application
  registry transaction; another program could still race an individual OS write.
- Interrupted `pending` and `restoring` entries are reconciled by read-only
  comparison on the next history/apply/restore request. Recovery never silently
  writes settings. Partial changes and unverified values remain visible.
- Restore attempts retain the original snapshot on failure. The same operation
  can be retried. A removed original power plan is not recreated implicitly.
- Forced restore preserves observed external values in a separate `.external`
  backup before writing. All backups remain after restoration.
- Bulk restore uses one lease, newest first. A failed/conflicting newer record
  blocks older operations on the same settings; unrelated settings can recover.
  Individual restore also enforces this ordering. Game files use a separate engine.
- The WPF Restore all changes command coordinates both journals. An error in
  Windows recovery does not suppress attempting game-file recovery, or vice versa.
- The journal stores only the fixed settings above and operation metadata. It is
  never uploaded. It contains no account credentials or license tokens.

Status values: `pending`, `applied`, `unchanged`, `failed`, `partial`, `conflict`,
`interrupted`, `needs_attention`, `restoring`, `restored`, `restore_failed`.

## Read-only measurements

- CPU model is read from the hardware registry; CPU activity uses two
  `GetSystemTimes` samples approximately 400 ms apart. On machines with more
  than 64 logical processors the Windows API has processor-group limitations.
- Memory uses `GlobalMemoryStatusEx`. Disks use fixed, ready `DriveInfo` volumes,
  and show space available to the current user. Removable media and network
  shares are excluded to avoid waking devices or blocking on disconnected shares.
- GPU labels come from `EnumDisplayDevicesW`, excluding mirroring drivers.
  These are device names; no temperature, VRAM-usage or driver metrics are inferred.
- Process CPU is sampled over approximately 400 ms, divided by available logical
  processors. The same process object and start time identify the sampled process
  instance. Memory is the current working set. Protected process fields can fail.
- Numeric `-1` means unavailable; UI must render it as unavailable rather than a
  negative percentage or memory amount. Missing device names use `Unavailable`.
- None of these reads apply a setting or terminate an application.

## Verification

`dotnet build desktop/Radiant.Windows/Radiant.Windows.csproj --configuration Release`

`dotnet run --project desktop/Radiant.Tests/Radiant.Tests.csproj --configuration Release`

The dependency-free console tests exercise an injected memory adapter and real
temporary journal files. They do not write the real registry, switch power plans,
or terminate processes. A successful core test run is distinct from target-OS
functional write acceptance.

Version 0.3 native acceptance applied and restored both animations and an existing
power plan on the development PC, reopening its journal before recovery. All
original values matched afterward. This does not certify all Windows configurations.
See `docs/optimizer-0.3-verification.md` for evidence and commands.

API references: [Power enumeration](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate),
[active power plan](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetactivescheme),
[CPU times](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes),
[physical memory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex),
[display devices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw).

Animation APIs: [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow),
[ANIMATIONINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-animationinfo).
