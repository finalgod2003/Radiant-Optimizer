# Original client and homepage views

## Primary client 0.4.1 (current homepage)

`radiant-client-overview.png`, `radiant-client-optimize.png`, and
`radiant-client-recovery.png` are rendered from the primary Windows WPF client
under `desktop/`, version 0.4.1, at 1200 × 820. The homepage uses these images
with the approved C / Midnight blue palette. No competitor screenshots are used.

The current views use the RO monogram from `public/brand/ro-mark.svg`, rendered
from the client's visual tree after the logo update. The retained verification
directory supplies the existing recovery history; regenerating these views does
not apply or restore any settings. See `docs/brand/README.md` for logo assets.

Overview uses actual sampled hardware information from the development PC, not
promised results for another machine. Game Boost shows the actual gaming and AC
power-mode controls. Recovery shows real native acceptance records: Gaming
essentials and AC mode were restored and verified. A generic folder label avoids
exposing private filesystem paths.

`radiant-client-account.png` shows the same client's enabled device activation
entry, with a Free installation connected to the local development website.
It does not depict a purchased or activated account.

Regenerate using the primary `Optimizer.exe --snapshot <image.png> --snapshot-page
<overview|optimize|recovery|account> --data-directory <directory>`. This reads and renders
the app's own visual tree; it does not apply optimization or open a desktop window.
Use the retained native verification folder for the historical Recovery view.
See `docs/optimizer-0.4-verification.md` for scope and release limitations.

## Premium controls (0.5.0)

The six `radiant-client-*` images (overview, optimize, games, cpu, recovery, account) were refreshed with our actual 0.5.0 WPF app. CPU and game lists reflect real local detection; no running game or fixture data was inserted into the website captures. Custom profiles and CPU controls are our implementation. Original competitor screenshots remain outside the served site.

## Archived read-only studies (0.2.0)

The older images below are rendered from this repository's independent read-only Windows WPF preview, version 0.2.0. UI, branding and application code are implemented in `desktop/preview/`. The full optimizer is maintained separately under `desktop/`. No third-party product screenshots are used.

- `radiant-overview-light.png` and `radiant-overview-dark.png`: the application's Overview page.
- `radiant-checks-light.png` and `radiant-checks-dark.png`: the application's System checks page.
- `radiant-hardware-light.png` and `radiant-hardware-dark.png`: the application's Hardware details page.
- `paper-client-desktop.png` and `atlas-client-desktop.png`: archived browser captures of the initial English studies with our 0.1 client. `cinema-client-desktop.png`: updated capture of the selected official homepage with the 0.2 client.

The WPF images use an actual read-only snapshot from the development machine. Hardware model names and sampled measurements are real; their values are examples from that machine, not promised results for another PC. The snapshot model excludes serial numbers, device IDs, hostnames, usernames and network identifiers. The web caption identifies the client as a read-only preview.

Regenerate the six application views with `./desktop/preview/build.ps1`, which publishes and verifies the client before rendering its WPF visual tree at 1280 × 840. A developer command is also available: `RadiantOptimizer.exe --render-preview <directory> <overview|checks|hardware> <light|dark>`. Each capture runs separately with a fresh hardware snapshot and verifies refresh, cancellation, filters and navigation. Native window borders are excluded from these website images.

The portable preview is unsigned and has been exercised on the development machine. It does not implement system tuning, paid activation or automatic updates. The user selected C / Midnight blue on 2026-10-10; the official homepage now uses that direction. The noindex studies remain available for reference.
