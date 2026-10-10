# Original client and homepage views

The product images are rendered from this repository's independent read-only Windows WPF preview, version 0.2.0. UI, branding and application code are implemented in `desktop/preview/`. The full optimizer is maintained separately under `desktop/`. No third-party product screenshots are used.

- `radiant-overview-light.png` and `radiant-overview-dark.png`: the application's Overview page.
- `radiant-checks-light.png` and `radiant-checks-dark.png`: the application's System checks page.
- `radiant-hardware-light.png` and `radiant-hardware-dark.png`: the application's Hardware details page.
- `paper-client-desktop.png` and `atlas-client-desktop.png`: archived browser captures of the initial English studies with our 0.1 client. `cinema-client-desktop.png`: updated capture of the selected official homepage with the 0.2 client.

The WPF images use an actual read-only snapshot from the development machine. Hardware model names and sampled measurements are real; their values are examples from that machine, not promised results for another PC. The snapshot model excludes serial numbers, device IDs, hostnames, usernames and network identifiers. The web caption identifies the client as a read-only preview.

Regenerate the six application views with `./desktop/preview/build.ps1`, which publishes and verifies the client before rendering its WPF visual tree at 1280 × 840. A developer command is also available: `RadiantOptimizer.exe --render-preview <directory> <overview|checks|hardware> <light|dark>`. Each capture runs separately with a fresh hardware snapshot and verifies refresh, cancellation, filters and navigation. Native window borders are excluded from these website images.

The portable preview is unsigned and has been exercised on the development machine. It does not implement system tuning, paid activation or automatic updates. The user selected C / Midnight blue on 2026-10-10; the official homepage now uses that direction. The noindex studies remain available for reference.
