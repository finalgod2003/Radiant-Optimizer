# Windows engine

`WindowsOptimizer` exposes the public methods consumed by the WPF app. It has no
network, account, payment, or licensing dependency. Restoring a saved change is
always available regardless of entitlement state.

## Implemented operations

All paths below are under **HKEY_CURRENT_USER**. The adapter maps fixed IDs to
paths itself; callers and journal entries cannot supply a registry path or script.

| Action ID            | Title              | Setting IDs / fixed values                                                                                                                               | Applied target                                |
| -------------------- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `game-mode`          | Game Mode          | `gaming.auto-game-mode`: `Software\Microsoft\GameBar\AutoGameModeEnabled`; `gaming.allow-auto-game-mode`: same key, `AllowAutoGameMode`                  | DWORD 1 / 1                                   |
| `game-recording`     | Game recording     | `gaming.game-dvr`: `System\GameConfigStore\GameDVR_Enabled`; `gaming.app-capture`: `Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` | DWORD 0 / 0                                   |
| `background-capture` | Background capture | `gaming.historical-capture`: `Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled`                                                | DWORD 0                                       |
| `power-plan`         | Power plan         | `power.active-plan`, Windows PowrProf API                                                                                                                | Existing enumerated GUID provided as `option` |

Gaming actions are exposed only on Windows build 19041 or later. An unexpected
existing registry type disables new applications, and a machine Game DVR policy
disables recording actions. This is a runtime capability check, **not a completed
Windows compatibility certification**. Target-OS writes and functional effects
still need isolated Windows acceptance tests; the preview UI requires an explicit
session opt-in. No settings are automatically applied on startup or scan.

All registry changes use the current user. No process elevation, arbitrary
commands, HKLM writes, power-plan creation, individual power-setting tuning,
process termination, service changes, or driver changes are implemented.

Power operations use `PowerEnumerate`, `PowerGetActiveScheme`,
`PowerReadFriendlyName`, and `PowerSetActiveScheme`; they do not shell out to
`powercfg`. A switch or restore must reference a plan that still exists, and its
active GUID is read back. Permission or policy failures are returned explicitly.

## Recovery semantics

- The engine takes a typed snapshot of every value before writing anything.
- The per-user `dataDirectory/recovery` journal is flushed to disk before the
  first mutation, and records intent before each subsequent write. Entries are
  replaced from a flushed temporary file in the same directory.
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

API references: [Power enumeration](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate),
[active power plan](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetactivescheme),
[CPU times](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes),
[physical memory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex),
[display devices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw).
