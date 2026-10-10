# Game Performance Implementation Plan

> Execute inline with the writing-plans and executing-plans workflows. The user has requested more real game performance features, backup before every change, and one-click recovery. Continue the existing approved client without another design approval round.

**Goal:** Deliver client 0.4.0 with usable Free gaming adjustments and unified recovery.

**Architecture:** Extend the existing allowlisted Windows adapter and durable transaction engine. Keep exact-byte game configuration backups and add ordered bulk restore to the game engine. The WPF app coordinates the two independent recovery engines and reports partial failures without hiding either domain.

**Tech stack:** Existing .NET 10, WPF, Windows APIs; current website for download metadata and original app screenshots.

## Global constraints

- Back up source before edits and every affected setting/file before writes. Keep 0.3 journals and releases usable.
- No FPS claims without measurement. Explain quality, capture, heat and battery tradeoffs. No service/driver/security/network tuning or game injection.
- New basic Performance presets are Free; existing Competitive/Balanced presets retain their entitlement rules. Recovery remains Free.
- Unsupported Windows APIs, policies, unrecognized game schemas and running games block the affected action with a reason.
- No commit/deployment. Use the current feature branch and preserve existing uncommitted work.

## Tasks

- [x] Archive the client source to `artifacts/source-backups/client-before-game-performance-20261010-150907.zip`.
- [x] Game profiles: extend `GameProfiles.cs` with `performance` and bounded `performance-<fps>` options (60/90/120/144/165/240/360 for Fortnite/VALORANT). Preserve legacy preset targets so old journals validate. CS2 changes validated VSync/MSAA only; Fortnite lowers existing shadow/effects/postprocess/foliage quality; VALORANT lowers existing material/detail. Preserve resolution, input settings, unknown fields and original encoding.
- [x] Game recovery: add `RestoreAllAsync(CancellationToken)` to `GameProfileEngine`, one lock newest-first, stop older changes to the same file on conflict/failure while restoring unrelated files. Prevent out-of-order individual restores. Tests cover exact bytes, multiple changes, repeated recovery, conflicts and unavailable/running games.
- [x] Windows gaming profile: `gaming-ready` combines Game Mode on, game recording off and background recording off into one pre-backed-up operation. Expose its real current/target values and the loss of Windows game clips. Existing individual controls remain.
- [x] Windows AC mode: `gaming-power` uses documented `PowerGet/SetUserConfiguredACPowerMode` to request Best Performance when plugged in. Back up the previous GUID and verify readback. Battery power mode stays untouched; firmware/thermal policy may override the effective result. Unsupported API/power source fails closed.
- [x] WPF: lead Game Boost with gaming actions; put animation preferences in a collapsed section. Add Free Performance preset selectors and exact diff review. Route game-file writes through the existing mutation/close guard. Recovery → Restore all changes covers Windows and game-file journals and reports each domain.
- [x] Acceptance: expand deterministic tests; use an explicit native gaming roundtrip command that always restores actual Windows writes, with fresh disk journal reload. Game-file acceptance uses isolated generated fixtures, never a user's live game configuration without choosing it.
- [x] Package 0.4.0, read-only-render the updated WPF pages, update download/feature copy and verification record, run `pnpm desktop:test`, `pnpm desktop:build --free-preview`, TypeScript and `pnpm build`; verify ZIP/hash.

## API basis

[AC power mode getter](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode), [AC power mode setter](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetuserconfiguredacpowermode). The setter records the user's requested mode; it does not guarantee the effective thermal/performance policy.
