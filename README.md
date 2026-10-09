# Radiant Optimizer

A bilingual browser-game directory and gaming toolkit built on the ShipAny TanStack template.

## Run locally

Requires Node.js 22.12+ and pnpm 11.19 (pinned in package.json).

1. Install dependencies: `pnpm install`.
2. Copy `.env.example` to `.env.development`. Set a random `AUTH_SECRET` if using the template auth features. The current workspace already has a generated local secret.
3. Initialize the local SQLite database: `pnpm db:push`.
4. Run `pnpm dev` and open http://localhost:3000.

The predev script prepares translations on first boot. Home, game library and calculators need no login. No admin account is created automatically.

## Validate and build

- `pnpm exec tsx --test tests/gaming-tools.test.ts`
- `pnpm exec tsc --noEmit` (after the first dev/build has generated translations and routes)
- `pnpm build`
- `pnpm start` (supply production environment variables)

Before a production build, configure `VITE_APP_URL` in `.env.production` for correct canonical and sitemap URLs. Configure `AUTH_SECRET` and the database if exposing the template account/admin functionality. The site has not been deployed.

## What is included

- English at `/`; Chinese at `/zh`.
- Homepage H1: **Radiant Optimizer**.
- 25 source destinations, searchable categories, alphabetical sorting and local bookmarks.
- 17 embedded-launch entries and 8 original-site entries. Third-party availability is not guaranteed.
- Game detail pages with click-to-load, fullscreen, reload, stop and original-site fallback.
- FPS/frame-time and same-game DPI sensitivity calculators.
- Responsive layout, localized metadata and sitemap, source attribution.

This is an independent community directory, not the official Windows optimizer. The game list comes from [the supplied @RadiantOpti post](https://x.com/RadiantOpti/status/2108068632991256995). Some hosts forbid embedding or require their own tab, and some ports require your own game files. See [implementation notes](docs/implementation-notes.md) and [game sources](docs/game-sources.md).

## Template and Git

The original ShipAny auth, billing, CMS and admin modules remain available. Their original documentation is retained in [docs/shipany-template-readme.md](docs/shipany-template-readme.md). The project repository is [finalgod2003/Radiant-Optimizer](https://github.com/finalgod2003/Radiant-Optimizer), with `main` as the primary branch and `origin` as its remote. The template remote remains named `upstream`.

The original [ShipAny license](LICENSE) and attribution are preserved. Game artwork belongs to the respective creators.
