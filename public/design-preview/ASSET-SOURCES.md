# Original client and homepage views

## Primary client 0.3.0 (current homepage)

`radiant-client-overview.png`, `radiant-client-optimize.png`, and
`radiant-client-recovery.png` are rendered from the primary Windows WPF client
under `desktop/`, version 0.3.0, at 1200 × 820. The homepage uses these images
with the approved C / Midnight blue palette. No competitor screenshots are used.

The current views use the RO monogram from `public/brand/ro-mark.svg`, rendered
from the client's visual tree after the logo update. Regenerating these views
does not apply or restore any settings. See `docs/brand/README.md` for logo assets.

Overview uses actual sampled hardware information from the development PC, not
promised results for another machine. Windows settings shows the actual optional
animation controls. Recovery shows real native acceptance records: all three
operations were restored and verified. A generic custom-folder label avoids
exposing private filesystem paths.

Regenerate using the primary `Optimizer.exe --snapshot <image.png> --snapshot-page
<overview|optimize|recovery> --data-directory <directory>`. This reads and renders
the app's own visual tree; it does not apply optimization or open a desktop window.
Use the retained native verification folder for the historical Recovery view.
See `docs/optimizer-0.3-verification.md` for scope and release limitations.

## Archived read-only studies (0.2.0)

The older images below are rendered from this repository's independent read-only Windows WPF preview, version 0.2.0. UI, branding and application code are implemented in `desktop/preview/`. The full optimizer is maintained separately under `desktop/`. No third-party product screenshots are used.

- `radiant-overview-light.png` and `radiant-overview-dark.png`: the application's Overview page.
- `radiant-checks-light.png` and `radiant-checks-dark.png`: the application's System checks page.
- `radiant-hardware-light.png` and `radiant-hardware-dark.png`: the application's Hardware details page.
- `paper-client-desktop.png` and `atlas-client-desktop.png`: archived browser captures of the initial English studies with our 0.1 client. `cinema-client-desktop.png`: updated capture of the selected official homepage with the 0.2 client.

The WPF images use an actual read-only snapshot from the development machine. Hardware model names and sampled measurements are real; their values are examples from that machine, not promised results for another PC. The snapshot model excludes serial numbers, device IDs, hostnames, usernames and network identifiers. The web caption identifies the client as a read-only preview.

Regenerate the six application views with `./desktop/preview/build.ps1`, which publishes and verifies the client before rendering its WPF visual tree at 1280 × 840. A developer command is also available: `RadiantOptimizer.exe --render-preview <directory> <overview|checks|hardware> <light|dark>`. Each capture runs separately with a fresh hardware snapshot and verifies refresh, cancellation, filters and navigation. Native window borders are excluded from these website images.

The portable preview is unsigned and has been exercised on the development machine. It does not implement system tuning, paid activation or automatic updates. The user selected C / Midnight blue on 2026-10-10; the official homepage now uses that direction. The noindex studies remain available for reference.
