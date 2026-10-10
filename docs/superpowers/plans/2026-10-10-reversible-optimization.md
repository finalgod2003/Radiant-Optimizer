# Reversible Windows optimization — version 0.3.0

The user requested real Windows adjustments, backup before every change, and one-click recovery. This work extends the primary client under `desktop/`; the independent read-only study under `desktop/preview/` remains unchanged.

## Scope

- Add optional interface and minimize/restore animation adjustments through documented Windows APIs, with current/proposed values and pre-apply review.
- Preserve existing game preferences, power plans, game profiles, monitoring, and entitlement rules.
- Save originals durably before writing; read back every change; keep failed/interrupted records.
- Restore all recorded Windows changes newest first. External conflicts require review; unrelated settings can still recover.
- Retain backups after restoration. These cover recorded settings, not full Windows images. Game-file recovery remains separate on the same page.
- Continue C / Midnight blue, original client images, and English additions. No commit or deployment.

## Completed work

- [x] Save source backups under `artifacts/source-backups/` before edits.
- [x] Extend the Core transaction engine with bulk restoration and ordering/conflict guards.
- [x] Store immutable, checksummed `.original` files separately from execution status; preserve external values before explicitly conflicting restores.
- [x] Add allowlisted `SystemParametersInfoW` adjustments. Restore exact saved power GUIDs, including valid schemes hidden from enumeration.
- [x] Add WPF controls, review, history, bulk results, and protection against closing/navigating during writes.
- [x] Verify native apply → reopen journal → restore for both animations and a power plan. All originals matched afterward.
- [x] Pass 125 desktop regression checks across Core, game profiles, monitoring, and licensing.
- [x] Package 0.3.0 as a Free preview without activation keys; retain Premium gates and disclose activation availability.
- [x] Replace homepage screenshots with primary-client renders and update download metadata/copy.

## Evidence

See [the verification record](../../optimizer-0.3-verification.md) for acceptance evidence, commands, and limitations. Normal builds and rendering never apply system changes; the native roundtrip command is explicit and separate.

API sources: [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow), [ANIMATIONINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-animationinfo), [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate), [PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme).
