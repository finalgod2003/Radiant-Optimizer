# Radiant Optimizer Implementation Plan

> Execute inline with the executing-plans workflow. The user's approved direction authorizes implementation in the current fresh checkout.

**Goal:** Deliver a runnable bilingual gaming directory and toolkit based on ShipAny TanStack, with the source post's games and a Radiant Optimizer H1.

**Architecture:** Typed static data feeds translated blocks and prop-driven UI components. Dedicated game routes defer external iframe loading until Play. Pure functions implement the calculators and localStorage stores favorites.

**Tech Stack:** TanStack Start, React 19, TypeScript, Paraglide, Tailwind CSS 4, Lucide, existing SQLite support.

## Global constraints

- Preserve ShipAny architecture and attribution; use `envConfigs.app_name` for the brand.
- Add all interface strings to both `messages/en.json` and `messages/zh.json`.
- Game destinations are a fixed HTTPS allowlist; no proxy or security-header bypass.
- English default, Chinese at `/zh`; public functionality needs no account.
- No deployment, payment setup or external publishing is requested.

## Tasks

- [x] Foundation: set app defaults, cross-platform install script, local environment, custom SVG mark, font packages, scoped dark CSS tokens. Install dependencies and initialize the local database.
- [x] Manifest: create `src/data/games.ts` with `Game` (`id`, `title`, `category`, `url`, `image`, `accent`, `mode`, `note`) and the 25 verified source destinations. Store sourced covers in `public/games/` with `docs/game-sources.md` attribution.
- [x] Homepage: implement translated `src/blocks/radiant/home.tsx` and shell, `src/components/radiant/game-card.tsx` and catalog, locale-aware route metadata, search/category/favorites controls and empty results. localStorage must tolerate blocked storage and malformed data.
- [x] Tools: implement `frameTime(fps): number | null` and `convertSensitivity(sensitivity, oldDpi, newDpi): number | null`; verify 240 FPS = 4.1667 ms and 0.4 at 800 DPI = 0.2 at 1600 DPI. Return null for invalid values. Build live calculator UI with accessible labels.
- [x] Player: implement `src/routes/games/$gameId.tsx`, translated player block and prop-driven player with Play, Reload, Stop, Fullscreen and original-site controls. Unknown IDs return 404. Known restricted/archived/unreachable destinations explain external launch.
- [x] Delivery: add catalog URLs to sitemap, remove demo SEO copy on the homepage, update README, format changed files, run build/type check/numeric tests, verify real browser flows at desktop/tablet/mobile sizes, run the repository security scan, and leave the local preview available.

## Verification commands

`pnpm build` must exit zero. `pnpm exec tsc --noEmit` must exit zero or identify a demonstrated upstream issue. `pnpm exec tsx --test tests/gaming-tools.test.ts` verifies meaningful calculator edge cases. The browser must show a playable third-party game menu inside the frame and an explicit original-site state for a known blocked host.
