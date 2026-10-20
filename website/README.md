# Kumo website

A single static page. No framework and no dependencies: `build.mjs` fills in `src/index.html` from
`site.config.json` and copies everything to `dist/`, which can be served by any static host.

```powershell
cd website
node build.mjs       # production build into dist/
node serve.mjs       # preview dist/ on http://localhost:4321
```

`npm run build` and `npm run dev` do the same. Node 18 or later is enough.

## New release

Edit `site.config.json`:

- `version` and `releaseTag` (`windows/v<version>`; the build refuses anything else). Download links point to the
  `Kumo-<version>-win-x64-setup.exe` and `-win-arm64-setup.exe` assets of that release.
- `siteUrl`, once the site has an address (for example a GitHub Pages URL). Until then the canonical link and the
  absolute Open Graph image are left out, because social previews need a full URL.

Update the feature list in `src/index.html` only for what the release actually ships; it follows the README.

## Assets

`src/assets` holds optimized copies of the repository's own artwork:

| File | Source |
| --- | --- |
| `kumo.webp` | `assets/mascot/kumo-processing.svg` |
| `kumo-64.png`, `favicon-32.png`, `apple-touch-icon.png` | `assets/mascot/kumo-idle.svg` |
| `radial-menu.webp` | `assets/github/example1.png`, cropped to the menu so the rest of the desktop is not shown |
| `crop-editor.webp` | `assets/github/example2.png` |
| `og.jpg` | `assets/github/banner.png`, fitted to 1200 × 630 |

The radial menu capture is 340 px wide at 100 % scaling. A sharper capture at 200 % scaling (or a short screen
recording of a drag) would replace it best.
