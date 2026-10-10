# RO logo

The mark combines a chamfered O ring with a geometric R. A diagonal opening
continues the R's leg, suggesting forward motion. Ice blue (`#A4C8F4`) and
midnight (`#101C2B`) match the product's existing palette.

- Master: `public/brand/ro-mark.svg` (transparent ice blue).
- Light backgrounds: `public/brand/ro-mark-dark.svg`.
- Transparent PNG: `public/brand/ro-mark.png` (512 × 512).
- Application tile: `public/brand/app-icon.png` (512 × 512).
- Website default: `public/logo.svg`.
- Browser and Windows icons: `public/favicon.svg`, `public/favicon.ico`.
- Apple touch icon: `public/apple-touch-icon.png` (180 × 180, opaque).
- Presentation: `docs/brand/logo-preview.png`.

The ICO contains 16, 20, 24, 32, 40, 48, 64, 128 and 256 px frames.
Website headers respect the existing `VITE_APP_LOGO` override. The desktop
project embeds the mark and icon directly from these shared public assets.

Edit the master SVG, then regenerate the derived files on Windows:

```powershell
powershell -NoProfile -STA -File scripts/export-brand-assets.ps1
```

The export script renders only the two paths in the master SVG using Windows
Presentation Foundation. It has no third-party package dependency.
Keep the surrounding clear space and use at least 24 px for primary navigation.
The 16 px export is intended for browser and system chrome.

## Concept provenance

The initial concept was generated with the built-in imagegen tool, with
`transparent_background: true`. The production master was reconstructed as
two precise SVG paths so that browser and desktop exports share one geometry.
The unmodified concept is saved as `ro-concept.png` in this folder.

### Generation prompt

Use case: logo-brand
Asset type: original production logo concept for an existing Windows performance optimizer; a single monogram, suitable for subsequent native SVG implementation.
Primary request: Redesign the product logo using both capital letters "R" and "O", with a precise technology aesthetic. Both letters must be recognizable within one unified compact emblem.
Design: an engineered geometric O ring with clipped/chamfered corners surrounding a bold custom R. Integrate the R's diagonal leg into an intentional opening in the lower right of the O so the two forms feel connected, but keep generous negative space between the inner R and the outer O. The diagonal has a clean 45-degree speed-cut ending. Balanced strong silhouette, economical geometry, thick consistent weights that remain legible at 32px.
Style/medium: premium flat vector-style logo, crisp edges, precise geometric construction.
Color palette: one solid ice blue #A4C8F4, selected from the existing product's midnight-blue visual identity. No second color.
Composition: one single centered isolated emblem on a fully transparent square canvas. Emblem fills about 78% of width and height with even clear margins.
Constraints: original logo; both R and O evident; no other lettering, no brand wordmark, no tagline, no mockups, no shadows, no 3D, no texture, no glow, no gradients, no background tile, no tiny circuit traces, no extra orbit lines, no watermark. Flat single solid color on actual transparency.
