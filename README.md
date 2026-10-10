# Radiant Optimizer

An independent Windows optimizer preview with a native .NET client, bilingual product website, accounts, and device licensing, built on the ShipAny TanStack template. The existing game directory remains at `/games` and calculators at `/tools`.

The 0.2 Preview adds reversible game presets, live hardware history, and PresentMon frame capture with a separate overlay and local CSV export. Game formats and sensor availability are explicitly checked. NVIDIA driver tuning, broader game compatibility and bandwidth testing remain future work. See [preview delivery and operation notes](docs/optimizer-preview.md), [game profile support](docs/game-profile-support.md), and [monitoring support](docs/monitoring-support.md) for the exact scope.

## Run locally

Requires Node.js 22.12+ and pnpm 11.19 (pinned in package.json).

1. Install dependencies: `pnpm install`.
2. Copy `.env.example` to `.env.development`. Set a random `AUTH_SECRET` if using the template auth features. The current workspace already has a generated local secret.
3. Initialize the local SQLite database: `pnpm db:push`.
4. Run `pnpm dev` and open http://localhost:3000.

The predev script prepares translations on first boot. Product pages, game library and calculators need no login. No admin account is created automatically. Run `pnpm rbac:init` and use `pnpm rbac:assign` for an explicitly selected account when configuring the admin panel.

For desktop activation, run `node scripts/setup-desktop-keys.mjs` once to create persistent local signing keys without printing them. Install the .NET 10 SDK (or set `DOTNET_HOST_PATH`), then run `pnpm desktop:build`. The ZIP and its SHA-256 manifest appear in `public/downloads/`; the website enables its download link only when a valid release exists. The client trusts the public keys embedded at build time. Set the intended website origin before packaging: the package built in this workspace connects to `http://localhost:3010`.

## Validate and build

- `pnpm exec tsx --test tests/*.test.ts`
- `pnpm desktop:test` (Windows; recovery, licensing, game-file and monitoring checks)
- `pnpm exec tsx scripts/with-env.ts tsx tests/desktop-activation.run.ts http://localhost:3010` (with the local server running against `data/optimizer-dev.db`)
- `pnpm exec tsc --noEmit` (after the first dev/build has generated translations and routes)
- `pnpm build`
- `pnpm start` (supply production environment variables)

The production website is deployed at <https://radiantoptimizer.xyz>. Cloudflare Workers Builds follows `main`, using the committed public production profile and existing Cloudflare secrets. See [Cloudflare deployment settings and verification](docs/cloudflare-deployment.md) for the build commands, release download binding, and migration workflow.

For other deployment environments, configure `VITE_APP_URL` in `.env.production` for correct canonical and sitemap URLs. Configure `AUTH_SECRET` and the database before exposing accounts.

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

- Native Windows x64 client: real hardware/process readings, CPU/RAM history, supported GPU sensors, bounded Ping/DNS tests and in-window input tools. NVIDIA NVML provides optional GPU load, temperature and memory; Windows GPU counters provide available fallback readings. CPU temperature is not included.
- Free game discovery and preview for narrow CS2, Fortnite and VALORANT file formats. Competitive and balanced presets preserve unrelated content. Unknown versions, missing required keys, running games, read-only paths and external changes block application. See the support document before using a preset; actual game-launch compatibility is not assumed.
- Premium adds game preset application, existing power-plan switching, PresentMon recording, a separate floating overlay and local CSV export. Frame statistics report application presentation cadence, including dropped frames, not physical-display FPS or end-to-end input latency. The overlay is intended for windowed or borderless play.
- Three experimental free user-level settings (Game Mode, game recording and background capture). System and game writes default off each session and require confirmation.
- Durable original-value journal, exact game-file backups, readback verification and conflict-aware restoration. Recovery is always free. Monitoring data and game backups stay local and are not automatically uploaded.
- Website sign-in, product/download pages, short-code device approval, license/device management and admin revocation.
- Stripe one-time payment confirmation and refund revocation; single active device, seven-day signed offline lease, DPAPI-protected local credentials. No real merchant payment has been performed.
- Premium lifetime access is priced at USD 19.90. Checkout requires license signing and a configured Stripe or Waffo provider; Waffo production sales also require merchant approval and production credentials. See [Waffo setup](docs/waffo-payment-setup.md) for the sandbox verification and remaining launch steps.

The retained browser library at `/games` (Chinese: `/zh/games`) includes:

- 25 source destinations, searchable categories, alphabetical sorting and local bookmarks.
- 17 embedded-launch entries and 8 original-site entries. Third-party availability is not guaranteed.
- Game detail pages with click-to-load, fullscreen, reload, stop and original-site fallback.
- FPS/frame-time and same-game DPI sensitivity calculators.
- Responsive layout, localized metadata and sitemap, source attribution.

This project is independently implemented and is not affiliated with the original Radiant Optimizer. The retained game list comes from [the supplied @RadiantOpti post](https://x.com/RadiantOpti/status/2108068632991256995). Some hosts forbid embedding or require their own tab, and some ports require your own game files. See [directory implementation notes](docs/implementation-notes.md) and [game sources](docs/game-sources.md).

## Template and Git

The original ShipAny auth, billing, CMS and admin modules remain available. Their original documentation is retained in [docs/shipany-template-readme.md](docs/shipany-template-readme.md). The project repository is [finalgod2003/Radiant-Optimizer](https://github.com/finalgod2003/Radiant-Optimizer), with `main` as the primary branch and `origin` as its remote. The template remote remains named `upstream`.

The original [ShipAny license](LICENSE) and attribution are preserved. Game artwork belongs to the respective creators.
