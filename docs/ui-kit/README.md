# Voxt UI Kit

A single-page, self-contained snapshot of the Voxt design system —
colors, typography, icons and core components — that opens from a link
without running the app.

Open `index.html` directly (`file://`) or serve the folder over HTTP
(GitHub Pages works as-is). Switch Light / Dark / Ash top-right. Click any
color or icon to copy its token / class.

## What's real vs. redrawn

The foundations are pulled **verbatim** from the frontend, so they can't
drift in value:

- **Colors** — the whole of `src/nodejs/styles/colors.css` is inlined into
  `<style id="tokens">`. Every swatch is resolved live with
  `getComputedStyle` for the active theme, so the hex you see is exactly
  what the app renders. No hard-coded colors.
- **Icons** — `src/nodejs/fonts/svgtofont/icon.css` is inlined into
  `<style id="icons">` and the grid is built from it; glyphs come from the
  real `icon.woff2`.
- **Fonts** — the real `TT Commons Pro` / `TT Commons Pro Mono` web fonts.
- **Type scale** — the `size × weight` matrix from `tailwind.config.js`.

The **component demos** (buttons, toggles, banners, inputs, tiles,
skeletons) are hand-authored markup styled with the real tokens. They
track colors and theming exactly, but their structure is a reproduction —
treat them as a reference, not the source of truth.

## Refreshing the snapshot

Re-copy the four source artifacts and re-inject, then re-verify:

```
# from the repo root
cp src/nodejs/fonts/svgtofont/icon.woff2            docs/ui-kit/assets/
cp src/nodejs/fonts/TT-Commons-Pro-*.woff2          docs/ui-kit/assets/   # Light/Regular/Medium/DemiBold/Mono
# then re-inline colors.css + icon.css into the #tokens / #icons blocks of index.html
```

`index.html` carries its own injection markers in the `<style id="tokens">`
and `<style id="icons">` blocks — ask Claude to regenerate it when the
sources change; that keeps the component demos and the inlined sources in
sync in one step.

## Files

```
docs/ui-kit/
  index.html                       self-contained kit (tokens + icons inlined)
  README.md
  assets/
    icon.woff2                     real icon webfont (269 glyphs)
    TT-Commons-Pro-*.woff2         real UI typeface (Light/Regular/Medium/DemiBold/Mono)
```

## Note on fonts

`TT Commons Pro` is a licensed typeface. It's bundled here so the snapshot
looks like the real product. Before publishing this folder publicly (e.g.
GitHub Pages), confirm the license permits it, or swap the `@font-face`
sources for the system sans fallback.
