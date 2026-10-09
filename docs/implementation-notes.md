# Radiant Optimizer implementation notes

This is a customization of the actual ShipAny TanStack repository at base commit `79bf9d7122399f6093e3608ca0a1ff99dad60067`.

## Features

- English homepage at `/`, Chinese at `/zh`, with a Radiant Optimizer H1.
- The 25 destinations in the source post, with search, category filtering, alphabetic sorting and local favorites.
- Per-game pages at `/games/<id>` and `/zh/games/<id>`, with lazy launch, iframe sandbox, reload, stop, fullscreen and original-site links.
- Live FPS/frame-time and same-game DPI sensitivity calculators.
- Responsive design, accessible control labels, visible keyboard focus, reduced-motion support, canonical/hreflang/OG metadata and a bilingual sitemap.
- Original template business modules preserved; public game/tool use requires no account.

## Third-party behavior

Seventeen entries offer an embedded launch. Eight use an external launch because of known host restrictions, isolation requirements, availability limitations, an archived source or a collection destination. This count describes launch behavior, not a guarantee that every third-party game currently works. The browser can confirm the iframe document loaded but cannot reliably distinguish all cross-origin failure pages from a working game; an original-site link is always available, and a delayed help link appears after 15 seconds.

`global-terror.net` disallows all framing. `wasmarcade.com` requires same-origin framing. BO1 Zombies and The Simpsons send isolation headers; Halo also uses the dedicated-tab path. MW2 returned 429 and Hedgewars was unreachable during initial checks. The GTA 5 entry points to the archived URL in the original post. `ut.pieter.com` is Urban Terror, despite being labeled Unreal Tournament in the post. Game files are not redistributed.

## Template compatibility fixes

The original POSIX postinstall command has been replaced with a Node script so installation works on Windows. Build-script approvals were migrated from the obsolete package.json pnpm setting to a package-specific `allowBuilds` map for pnpm 11. On a fresh checkout, predev generates Paraglide output before Nitro imports it, avoiding a first-boot race.

## Main files

- `src/data/games.ts`: source destinations and launch policy
- `src/blocks/radiant/`: translated page sections
- `src/components/radiant/`: reusable cards, player and calculators
- `src/styles/radiant.css`: scoped visual system and responsive rules
- `src/lib/gaming-tools.ts`: validated pure calculations
- `tests/gaming-tools.test.ts`: numeric and catalog integrity tests
- `docs/game-sources.md`: preview image attribution

Favorites remain on the visitor's device. They are validated against the known catalog, tolerate unavailable storage and are written only after hydration.

## Verification (October 9, 2026)

- Production build, TypeScript check and all 5 calculator/catalog tests pass.
- Browser checks cover search, empty results, persistent favorites, English/Chinese switching, numeric input validation, show-all, embedded launch, reload, fullscreen and external-only game states.
- Responsive checks at 390, 768 and 1440 pixels show no horizontal document overflow. The mobile menu opens and closes after following a section link.
- CS Surf loads its map selector and renders the Boreas 3D scene inside the site. Kino der Toten loads its menu, but its host reports missing texture resources and the map remains loading. No claim is made that every third-party game is currently playable.
- English and Chinese homepage/game routes return 200; unknown game IDs return 404. Each public page has one canonical and its three appropriate language alternates. The sitemap contains 52 localized URLs.
- The repository security scan passes. A manual review found no added secret exposure, arbitrary iframe URL input, proxy, unsafe HTML rendering or changes to auth/payment logic.

| Level | Location                                                         | Finding     | Action                   |
| ----- | ---------------------------------------------------------------- | ----------- | ------------------------ |
| Clean | Changed source, configuration, documentation and sourced artwork | No findings | Ready for local delivery |
