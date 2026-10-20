# Kumo website

The Kumo website, built with Next.js as a fully static export: no server, no database, nothing to run in production.
Pages: the home page with an interactive demo of the radial menu, and a changelog read from the GitHub releases.

```powershell
cd website
npm install
npm run dev      # http://localhost:4321
npm run build    # static site in out/
```

`out/` can be uploaded to any static host (GitHub Pages, Cloudflare Pages, Netlify).

## New release

1. Edit `lib/site.ts`: `version` and `releaseTag` (`windows/v<version>`). The download buttons point to the
   `Kumo-<version>-win-x64-setup.exe` and `-win-arm64-setup.exe` assets of that release.
2. Update the feature list in `app/page.tsx` and the demo menus in `lib/menu.ts` only for what the release ships.
3. Rebuild. The changelog is read from the GitHub API during the build, so it includes every published
   `windows/v*` release, newest version first by SemVer. Set `GITHUB_TOKEN` if the build machine hits GitHub's rate
   limit; if GitHub cannot be reached, the page links to the releases on GitHub instead of failing the build.

Set `url` in `lib/site.ts` once the site has an address; until then the canonical link and the absolute Open Graph
image are left out, because social previews need a full URL.

## The demo

`components/DesktopDemo.tsx` recreates Kumo's radial menu on a pretend desktop. `lib/menu.ts` mirrors the app: the
items for one JPG, PNG or PDF, their labels and descriptions, the 350 ms rest that opens a category, and the names of
the files Kumo writes. Nothing is processed; actions that open a dialog in Kumo (Crop, Extract, Protect) say so.
It works with a mouse (drag, then tap Shift), with the keyboard (Enter or Ctrl+Alt+K on a file, arrow keys, Escape)
and on touch screens (tap a file).

## Assets

`public/assets` holds optimized copies of the repository's own artwork, and `public/mascot` the app's mascot states
from `src/Kumo.App/Mascot/Images`.

| File | Source |
| --- | --- |
| `kumo.webp` | `assets/mascot/kumo-processing.svg` |
| `kumo-64.png`, `favicon-32.png`, `apple-touch-icon.png` | `assets/mascot/kumo-idle.svg` |
| `radial-menu.webp` | `assets/github/example1.png`, cropped to the menu so the rest of the desktop is not shown |
| `crop-editor.webp` | `assets/github/example2.png` |
| `strawberry-thumb.webp` | `assets/github/example2.png`, the photo only |
| `og.jpg` | `assets/github/banner.png`, fitted to 1200 × 630 |
