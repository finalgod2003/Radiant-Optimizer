# Windows Client 0.2 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make the approved English, read-only client more useful with detailed hardware, explainable next steps, filtering and readable local reports.

**Architecture:** Keep Windows collection unchanged and use its existing real readings. Add pure presentation/report models in Core; WPF displays dedicated hardware groups and filtered checks. All screenshots and package links come from the rebuilt executable.

**Tech Stack:** .NET 10, C#, WPF, existing System.Management collector.

## Global constraints

- The user's continuing client scope inherits English UI, real readings and read-only Windows checks. Do not add system tuning, payment, activation or telemetry.
- Missing readings remain Unknown. Report content is encoded before HTML export; reports use no external assets or scripts.
- Dark theme defaults to the selected website direction. Preserve light theme.
- Keep reports user-initiated; no automatic hardware history or identifiers.

## Implementation

- [x] Add `CheckGuidance.For(CheckResult)` in `desktop/preview/Radiant.Core` with status-specific next steps. Add `CheckFilter` (`All`, `Attention`, `Unknown`) and `CheckSelection.Filter` for testable selection. Add `HardwareDetails.Create(HardwareSnapshot)` for CPU, GPU drivers, memory, all fixed drives, Windows and session details; missing data is explicit.
- [x] Add `Report.Html(product, version, snapshot)` alongside JSON. Encode every dynamic value with `WebUtility.HtmlEncode`; use an offline document, real timestamp, hardware groups, evidence, explanations and next steps. Tests cover malicious hardware names, no external resources, full drive/driver coverage, unknown data and filter behavior.
- [x] Update MainViewModel and MainWindow: Hardware page, driver/all-drive details, check status totals and filters, expandable advice, useful empty state, dark default, F5 refresh, cancellation and readable error states. HTML/JSON save choices share the existing user-chosen SaveFileDialog; protect against overlapping exports or physical scans.
- [x] Bump `desktop/preview/Directory.Build.props` to 0.2.0, derive the archive version from that file in `desktop/preview/build.ps1`, update README, render hardware alongside overview/checks in light/dark. The actual EXE developer render verification exercises navigation, filters, refresh and both report serializers.
- [x] Run core tests, publish and verify real hardware, inspect original WPF screenshots, check package and public-copy hashes. Record final results.

## Verification

- 40 deterministic core checks passed, including thresholds, filtering, all-drive/driver inventory, unknown data, HTML encoding and report contents.
- Release compilation passed with no warnings or errors. The self-contained executable read actual CPU, memory, three graphics adapters and the Windows drive. Five checks ran; the absent explicit Game Mode preference correctly remained Unknown.
- All six WPF render sessions verified live refresh, cancellation preserving the previous snapshot, filters, page navigation and branding. Reviewed dark hardware and light/dark checks layouts.
- Version 0.2.0 archive contains only `RadiantOptimizer.exe` and `README.md`. Local verification reports remain outside the distributable folder.
- Final ZIP SHA-256: `f741524f44f4304ad55f49ab51e9b9ee6cc35a790391396299d472c1690f1971`. The local website serves the same archive.
- The release remains an unsigned read-only preview verified on this development machine; no native OS automation, signing, installer or public deployment was performed.
