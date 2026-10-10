# Monitoring support and measurement definitions

Updated: 2026-10-10. This is the implementation boundary of the second local Windows preview, not a guarantee of compatibility with every game or graphics driver.

## Read-only sensors

| Reading                                         | Source                                                                      | Behavior when unavailable                                                  |
| ----------------------------------------------- | --------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| CPU utilization, physical RAM used/total        | Existing `Radiant.Windows` reader: `GetSystemTimes`, `GlobalMemoryStatusEx` | Nullable value and reason; no invented zero                                |
| GPU utilization                                 | Windows PDH English `GPU Engine(*)\Utilization Percentage` counters         | Warm-up, missing/invalid counters and access failures remain unavailable   |
| Dedicated/shared GPU memory in use              | Windows `GPU Adapter Memory(*)` counters                                    | Missing counters remain unavailable                                        |
| GPU identity and dedicated capacity             | DXGI adapter descriptor and adapter LUID                                    | Unknown counter-only adapters retain their LUID; no guessed product name   |
| NVIDIA temperature, driver utilization and VRAM | NVIDIA driver NVML read APIs                                                | Each metric checks its return code; unsupported sensors remain unavailable |

GPU engine samples are grouped by adapter LUID, physical GPU and engine index. We sum process usage within the same engine, then use the busiest engine percentage, clamped to 0–100. We do not sum percentages across different engines or GPUs. Adapter memory counters are used instead of summing process memory, because the same allocation can be shared between processes. PDH paths are language-neutral and work on the tested Chinese Windows installation. Every array allocation is bounded and volatile counter lists get at most three retrieval attempts.

NVML is loaded only from the absolute Windows System32 or Program Files/NVIDIA Corporation/NVSMI driver location. Reparse paths are rejected; dependency search is restricted to the driver directory and System32. The client does not download/install a sensor driver, search PATH/current directories, or invoke NVIDIA control/set APIs. NVML's basic temperature and memory v1 functions are retained for compatibility with installed consumer drivers; each result is checked. New driver API deprecations do not imply that an existing basic sensor is guaranteed available.

An NVML row joins a Windows GPU only when its full GPU name uniquely matches on both sides. Duplicate or differently named cards remain separate, explicitly identified by source, instead of guessing a mapping. NVIDIA utilization and memory fill missing Windows readings. NVIDIA temperature and memory capacity come from NVML. AMD/Intel temperature and CPU temperature are not implemented. A system with driver history, remote display adapters, or hybrid graphics can expose additional counter-only adapter LUIDs.

Sensor history is a bounded ring (UI default 600 samples, maximum 3,600). Values have UTC sample timestamps. Sensor CSV has fixed columns, empty numeric cells for unavailable values and explicit reasons. Read-only hardware smoke on this host observed Intel GPU counters and NVIDIA GeForce RTX 3080 Ti Laptop GPU usage, temperature and VRAM. This verifies those interfaces on this host; it is not a benchmark.

## Frame capture

The capture helper is the official standalone **PresentMon 2.6.0 x64** console release, 980,320 bytes, SHA-256:

```text
b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af
```

The production entry point uses `AppContext.BaseDirectory/tools/PresentMon/PresentMon.exe`. Both size and SHA-256 must match before launch, even when tests supply an explicit path. The verified file is held open against rewriting/replacement during launch/capture. Reparse paths are rejected. Arguments use `ProcessStartInfo.ArgumentList`, not a shell. The process ID and original start time identify the selected process. A retained process handle detects its exit even if Windows later reuses that PID.

The helper uses a unique `Optimizer-<random GUID>` ETW session, `--process_id`, `--output_stdout`, `--v1_metrics`, `--timed`, `--terminate_after_timed`, `--terminate_on_proc_exit`, `--no_console_stats` and `--no_track_input`. It does not inject into a game, read keystrokes, use a hotkey, stop other named sessions, modify user groups, or request UAC elevation. A failure to start an ETW session is reported. PresentMon's own documented requirement is an administrator or a member of Performance Log Users; the app does not grant either permission.

The helper's CPU-present CSV measures application presentation cadence. It is **not display FPS, generated-frame FPS, game-engine simulation rate, network latency, or input-to-photon latency**. Dropped presentations are retained in these app-present measurements. OpenGL/Vulkan may have different kernel-observation semantics; PresentMon's source documentation explains their limits. A minimized, idle, protected or otherwise incompatible target can produce no usable frames.

Limits are 5–300 seconds, 60,000 accepted frames, 64 swapchains, 32 MiB stdout and 16 KiB per CSV record; diagnostics retain at most 8 KiB. The helper has its own time limit and the host has an independent limit eight seconds longer. Cancellation, selected-process exit, window cleanup or a bound violation stops the owned helper and attempts to stop only its unique ETW session. An unexpected cleanup result is surfaced. No default CSV is written by the helper: frames stay in memory until the user chooses a CSV export path. Abrupt OS/process termination is outside normal async cleanup; restarting the client does not enumerate or kill other sessions.

## Statistics

The strict parser accepts the named PresentMon v1 columns `ProcessID`, `SwapChainAddress`, `TimeInSeconds`, and `MsBetweenPresents`, irrespective of column order/case. It handles quoted commas/escaped quotes. It rejects malformed quoting, duplicate headers, wrong field counts, invalid IDs, non-finite numbers, negative timestamps, zero/negative frame times and unbounded values. Mixed PIDs, non-increasing timestamps within one swapchain and excessive unique swapchains are excluded by the accumulator.

For the selected PID, each summary chooses **one swapchain** with the most accepted samples (ties: most recent timestamp, then address). It never adds the FPS of multiple windows. The selected swapchain is part of every summary. The live summary uses the last five seconds of capture time; each eligible stream must have received a sample in the last three seconds. No recent/usable stream means a null live summary, not a retained stale FPS. A stopped capture has a separate historical summary.

For `n` accepted positive `MsBetweenPresents` samples `t₁ … tₙ` in the selected stream:

- Measured interval: `Σtᵢ / 1000` seconds. This is the sum of measured presentation intervals, not wall-clock session time.
- Average FPS: `1000 × n / Σtᵢ`.
- P95/P99 frame time: nearest-rank percentile, ascending index `ceil(p × n) − 1`.
- 1% low FPS: `1000 / mean(slowest ceil(0.01 × n) frame times)`, using at least one interval.
- Fewer than two valid intervals produce no summary. Small captures are not statistically stable benchmarks.

Frame CSV records all accepted swapchains individually, with PID, process start time, capture start time, received time and units in fixed column names. The consumer can reproduce the selected-stream statistics. Text cells are quoted, escaped and guarded against spreadsheet formula prefixes.

## Verification

```powershell
& "$env:USERPROFILE/.codex/tools/dotnet10/dotnet.exe" run --project desktop/Radiant.Monitoring.Tests -c Release
& "$env:USERPROFILE/.codex/tools/dotnet10/dotnet.exe" run --project desktop/Radiant.Monitoring.Tests -c Release -- --hardware-smoke
# Optional actual frame smoke using the official pinned helper and a render test process:
& "$env:USERPROFILE/.codex/tools/dotnet10/dotnet.exe" run --project desktop/Radiant.Monitoring.Tests -c Release -- --capture '<absolute PresentMon.exe>' <pid>
# Validate actual offline replay CSV against the maintainer's gold CSV:
& "$env:USERPROFILE/.codex/tools/dotnet10/dotnet.exe" run --project desktop/Radiant.Monitoring.Tests -c Release -- --verify-csv '<actual.csv>' '<gold.csv>'
```

Thirty-five deterministic checks cover CSV parsing, malformed values, frame/time/PID/swapchain bounds, stale data, statistic formulas, PDH aggregation, output cancellation/bounds, process identity, helper integrity, safe arguments, export escaping, CPU/RAM export without a GPU, independent sensor failures and disposal after an invalid PID or failed launch. All test files are in generated temporary directories; tests never write real games, registry, drivers or system configuration. The hardware smoke is read-only. Parent integration verification records actual PresentMon frame capture success or the precise permission limitation separately.

An offline integration replay used the official [v2.6.0 `Tests/Gold/test_case_2.etl`](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/Tests/Gold/test_case_2.etl), 4,745,988 bytes, SHA-256 `71194538cf20cce3ada1febce98dd12e978920db00adee7fdd1af2886983faf3`. The bundled official executable produced 97 v1 rows from this recorded trace. All 97 PID, swapchain, timestamp and frame-interval values matched the maintainer's [v1 gold CSV](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/Tests/Gold/test_case_2_v1.csv), and all four recorded PIDs passed through the parser and separate accumulators. Fixture/output files remain under ignored `artifacts/verification/presentmon-offline/`; they are not runtime samples or bundled product readings. This proves binary/CSV compatibility without requiring live ETW permission, not live capture on this host.

The live hidden D3D11 probe called Present successfully but PresentMon returned access denied (exit code 6): this host account has neither required ETW privilege. No group membership or elevation was changed. After the probe and offline replay, no process named `PresentMon` (our helper name) or `Optimizer-` ETW session remained. An unrelated pre-existing `PresentMon_x64` process remained untouched.

## Primary references

- [Microsoft: PDH English counter API](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw)
- [Microsoft: formatted wildcard counter arrays](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw)
- [Microsoft DirectX: GPU utilization and shared-memory accounting](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)
- [NVIDIA: NVML support and official driver library locations](https://docs.nvidia.com/deploy/nvml-api/nvml-api-reference.html)
- [NVIDIA: authoritative NVML C header](https://github.com/NVIDIA/go-nvml/blob/main/pkg/nvml/nvml.h)
- [PresentMon 2.6.0 release](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)
- [PresentMon console options and output](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)
- [PresentMon v1 measurement column definitions](https://github.com/GameTechDev/PresentMon/blob/v1.10.0/README-ConsoleApplication.md)
- [PresentMon ETW permissions and API limitations](https://github.com/GameTechDev/PresentMon/blob/v1.10.0/README.md)
