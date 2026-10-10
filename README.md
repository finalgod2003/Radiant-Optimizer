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

## Analytics

GA4 and self-hosted Plausible scripts are rendered in the HTML head on all pages. Configure them in `/admin/settings` → Analytics, or set these environment fallbacks in `.env.development` / `.env.production`:

```env
GOOGLE_ANALYTICS_ID=G-YOUR_MEASUREMENT_ID
PLAUSIBLE_DOMAIN=your-website.com
PLAUSIBLE_SRC=https://stats.blackholeenglish.com/js/pa-o45fCHNhYkEiUkUL7UY0A.js
```

Replace the example GA4 ID with your own. Plausible defaults to the self-hosted site-specific script above and includes the `plausible.init()` bootstrap. `PLAUSIBLE_DOMAIN` is optional for this script; if supplied, it is the website registered in Plausible, not `stats.blackholeenglish.com`. Legacy Plausible scripts remain supported and require a domain. GA4 stays disabled until a valid Measurement ID is set.

Database settings take precedence over environment values, including empty values. Restart the server after changing environment variables. Keep GA4 [enhanced measurement for browser history changes](https://developers.google.com/analytics/devguides/collection/ga4/single-page-applications) enabled for page views during client navigation; the Plausible script handles these automatically. Use [Plausible's installation checker](https://plausible.io/docs/plausible-script) and GA4 Realtime / DebugView on the deployed site to verify collection.

The production site at `https://radiantoptimizer.xyz` uses GA4 property `Radiant Optimizer` (`558223260`) and web stream `Radiant Optimizer Web` (`16096123901`), with Measurement ID `G-2ZJTXQ0EZ0`. Keep `GOOGLE_ANALYTICS_ID` set to this value in `.env.production` and `wrangler.jsonc` under `vars`. Leave it unset in local development to keep development visits out of the production reports.

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
