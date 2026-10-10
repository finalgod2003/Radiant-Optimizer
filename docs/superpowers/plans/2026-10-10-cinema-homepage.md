# Cinema Homepage Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Apply the user's selected C / Midnight blue design to the English product homepage, with working download and separate browser games.

**Architecture:** Extract the existing approved product sections into a reusable block. The official route renders the Cinema variant without the design switcher; the design study route continues to reuse the same sections. Keep the existing game catalog and player paths.

**Tech Stack:** TanStack Start, React, TypeScript, Paraglide, existing CSS and lucide icons.

## Global constraints

- User approved C on 2026-10-10. No new design selection is required.
- English first. H1 uses `envConfigs.app_name` and includes Radiant Optimizer.
- Download the actual version 0.2.0 Windows x64 package. Premium is planned and cannot charge users.
- Preserve other working changes; work on the existing feature branch. Do not deploy or commit.

## Implementation

- [x] Extract `ProductPage` from `src/blocks/radiant/design-previews.tsx` into `src/blocks/radiant/product.tsx`; expose the `variant` prop. Keep the selected CSS layout, screenshots and interactions. Official rendering uses `<ProductPage variant="cinema" />` with no preview bar.
- [x] Replace `src/blocks/radiant/home.tsx` with the official product composition and update `src/routes/index.tsx` metadata to product copy and our own screenshot. Move catalog entry to `src/routes/games/index.tsx` and `src/blocks/radiant/games.tsx`; update game shell and player back links to `/games`.
- [x] In `messages/en.json`, describe only implemented hardware details, explained checks and HTML/JSON reports as current features. Mark Premium as upcoming. Update the ZIP URL and screenshot version to 0.2.0. Keep any new keys in both locale files, with English fallback for this English-only iteration.
- [x] Add a mobile navigation control and a browse-all games link; align game shell colors with C. Update sitemap if one exists. Preserve game search, favorites, player routes and calculators.
- [x] Verify `pnpm exec tsc --noEmit`, `pnpm build`, real download ZIP signature, desktop and mobile navigation, screenshot dialog, game catalog filtering and back links. Save a screenshot of `/` for delivery.

## Verification

- `pnpm exec tsc --noEmit` and `pnpm build` passed. Build warnings concern existing chunk sizes and plugin timings; no build errors.
- Browser checks passed at 320, 390, 768 and 1440 pixels: no horizontal overflow; one product H1; no prototype switcher on the official page; correct canonical URL and product title.
- Mouse and keyboard download dialog, hardware screenshot selection, enlargement and Escape close verified. The served 0.2.0 ZIP returned HTTP 200 with the ZIP signature `504B0304`; its SHA-256 matches the packaged archive.
- Mobile navigation opens the separate game library; searching Quake returns three destinations; game detail returns to `/games#library`. Source game launch behavior is preserved.
- Screenshot saved to the task visualization folder as `radiant-cinema-homepage-v02.png`. C's overview thumbnail now reflects the official homepage.
