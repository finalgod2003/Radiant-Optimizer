# Radiant Optimizer — approved design

Approved by the user on 2026-10-09: English by default, Chinese available, a dark gaming site with the H1 “Radiant Optimizer”, the source post's browser games, and frame-time and mouse-sensitivity tools.

## Product

An independent community game directory and browser toolkit. Preserve the actual ShipAny TanStack template and its license/attribution. The site does not claim to change Windows settings, improve FPS, or be the official Radiant Optimizer product.

## Experience

- A compact navigation, oversized typographic hero, featured Kino der Toten launch card, searchable game grid, two working calculators, FAQ, and source attribution.
- Charcoal surfaces, acid-green actions, restrained borders, Barlow Condensed display typography and Manrope body typography. Real game preview assets where available, intentional typographic artwork elsewhere. Responsive at 1440, 768 and 390 pixels, with visible keyboard focus and reduced-motion support.
- English `/` and Chinese `/zh`, using the template's Paraglide translations and localized routing. Home H1 contains the exact phrase Radiant Optimizer in both locales.
- Twenty-five source destinations, filterable by category/name and by locally saved favorites. All destinations have stable game detail URLs.
- Embedded games load only after an explicit Play action. Provide fullscreen, reload, stop/back and an always-available original-site link. Never claim iframe load proves the game is playable. Escape releases game capture; the original-site option handles third-party failures.
- Sites with confirmed frame restrictions or cross-origin isolation needs use an explanatory external launch state. Do not proxy third-party assets or remove security headers. Games may require the visitor's own files.
- FPS → frame duration: `1000 / fps`. DPI conversion for the same game: `oldSensitivity * oldDpi / newDpi`; show unchanged eDPI. Reject empty, nonpositive and nonfinite inputs. No unsupported cross-game conversion factors.

## Source and verified limitations

Source: https://x.com/RadiantOpti/status/2108068632991256995 (read in the browser on 2026-10-09).

- `global-terror.net` returns DENY / frame-ancestors none; `wasmarcade.com/Fan` redirects to `/pc-games` and requires same-origin framing.
- `vel.gg/bo1z` and the Simpsons host send COOP/COEP isolation headers; use an original-site launch for these demanding ports unless actual embedding is validated.
- Modern Warfare 2 host returned HTTP 429; Hedgewars host could not be reached. Preserve original links and explain availability rather than fabricate success.
- The post calls `ut.pieter.com` Unreal Tournament, but the destination identifies itself as Urban Terror. Use its actual title and preserve that source-label note.
- GTA 5 source is a Wayback snapshot; clearly label it an archived link.

## Structure and persistence

Typed static game manifest in `src/data/games.ts`; reusable game cards and player in `src/components/radiant/`; translated composition in `src/blocks/radiant/`; homepage and `/games/$gameId` routes. Calculation functions are isolated in `src/lib/gaming-tools.ts`. No arbitrary user URLs enter iframe sources. Favorites use defensive localStorage parsing and do not write until hydration completes. Keep template backend modules intact.

## Delivery checks

Production build and TypeScript check; numerical edge-case tests; UI verification of filtering, favorites persistence, locale switch, calculator outputs, game launch/fullscreen/reload and external fallback; responsive inspection; metadata/H1/sitemap review. Record third-party limitations in the README.
