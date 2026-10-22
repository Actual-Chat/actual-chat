---
name: design-icon
description: |
  Design a new UI icon in Voxt's style as an SVG and add it to the icon font. Use whenever a new
  icon is needed and no existing glyph fits — "create an icon", "make an icon for X", "draw an
  icon", "design an icon", "we need an icon for …", "add an icon", "there is no icon for …", a
  request for an SVG glyph, or when a feature needs `<i class="icon-…">` and `src/nodejs/icons/`
  has nothing suitable. Covers matching the existing line width and contoured (not solid) style,
  the Figma "UI Icons" reference, and regenerating the font. Do not use it to pick an existing
  icon, or for multi-color art (that is a `.lit.ts` SVG component, see docs/ui/components.md).
allowed-tools:
  - Read
  - Grep
  - Glob
  - Bash
  - Edit
  - Write
---

# Design an icon

The icon pipeline itself — where sources live, how the CSS class is derived, why strokes must be
outlined, when the font is the wrong home — is documented in
[docs/ui/components.md → Icons](../../../docs/ui/components.md#icons). Read it first and do not
repeat it here. This skill is about drawing an icon that looks like it belongs.

## 1. Look before drawing

1. **Search for an existing glyph** by concept and by synonyms: `ls src/nodejs/icons | grep -i
   <word>`, and grep `src/nodejs/fonts/svgtofont/icon.css` for the class names. If one fits, use
   it and stop. A `-fill` variant exists for a few icons; the plain name is the contoured default.
2. **Open 4–6 neighbours** — icons of similar subject and similar complexity (a device, a person,
   a document, an arrow …) — and read their SVG source, not just the names. You are matching
   these, so take their numbers, not your memory of "what a 24px icon looks like".
3. **Look at the Figma reference** for more examples of the style:
   https://www.figma.com/design/5pkuJQCwslJnWWlqp3aqLa/UI-Icons?node-id=182-31443
   It is the Untitled UI "Line icons" set (General, Arrows, Charts, Users, …), the source of
   most of `src/nodejs/icons/`. The Figma MCP cannot read it ("you don't have edit access"), so
   don't use it. Open it with the **chrome1 MCP** (`new_page` with that URL, then
   `take_screenshot` **without `filePath`** — the MCP saves files in its own filesystem, not
   yours). It loads without signing in, view-only; close the cookie banner if it covers the
   canvas, and zoom the canvas (click the frame, `Shift+2`) to see individual glyphs. Use it only
   to see how the set draws the subject; read geometry from the local SVGs. Skip this step when
   the neighbours in `src/nodejs/icons/` already show the subject.

## 2. The style, measured

Numbers below were read from the path data of `calendar`, `clock`, `file`, `folder`, `image`,
`lock`, `message-square`, `phone`, `search` and `close`. Treat them as the spec; you don't need to
study the library again.

| Property | Value |
|---|---|
| Canvas | `viewBox="0 0 24 24"`, `width="24" height="24"` — 241 of 273 files. |
| Stroke width | **2 px**, everywhere (rings: outer r 10, inner r 8; bars 2 wide; `20×20` icons too). Never 1.5 or 3. |
| Live area | x and y within **2…22**; boxes are usually 2…22 wide and 3…21 or 4…20 tall. `close` is smaller (6…18), `search` 3…21 — small glyphs stay small, they are not scaled up. |
| Outer corner radius | **3** on boxes (`message-square`, `image`, `lock`); **2.5** on `file`, `folder`. Use 3. |
| Inner corner radius | **outer − 2** (stroke width): **1** for a 3 outer, **0.5** for a 2.5 outer. Corners are concentric: the inner contour is the outer one offset by 2. |
| Sharp corners | None. Inner radius never below 0.5; no mitered points anywhere. |
| Line ends | Round cap = semicircle of **r 1** (half the stroke). Dots are solid circles of **r 1**, as in the `message-square` ellipsis. |
| Gaps | ≥ 2 px between separate strokes, so counters stay open at 16 px. |
| Fill | **Contoured, not solid.** A circle is a ring, a document is an outlined page. Solid only for dots. A fully filled glyph is a separate `<name>-fill` icon, only when asked. |
| Color | Irrelevant — the font is monochrome. |

Rule of thumb: pick the **centre line** of every stroke on a whole or half pixel, draw it with
`stroke-width: 2`, and let the outline come out as ±1 around it. Corner radius on the centre line
is **2** (→ outer 3, inner 1). That reproduces every box above.

### Worked example: a square

Centre-line square from (4,4) to (20,20), corner radius 2, stroke 2 — this is the shape to start
from for anything boxy (card, window, image frame, checkbox):

```xml
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <rect x="4" y="4" width="16" height="16" rx="2" stroke="currentColor" stroke-width="2"/>
</svg>
```

After outlining, it is two contours: outer 3…21 with radius 3, inner 5…19 with radius 1.
Hand-written, that is (filled paths only, what the font wants — the inner contour winds the
opposite way, so it stays a hole under the nonzero rule):

```xml
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <path fill="currentColor" fill-rule="evenodd" clip-rule="evenodd"
        d="M6 3h12a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3Zm0 2a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1Z"/>
</svg>
```

A line that leaves the square (a tick mark, a pointer) is a 2 px band with round caps (r 1)
that overlaps the border: union it into the same path rather than leaving two overlapping shapes.
Open the real file (`message-square`, `lock`, `folder`, …) when the subject is close to one.

## 3. Draw it

Write the SVG by hand or generate it from strokes, then **outline it**. The font generator drops
stroke geometry (docs/ui/components.md step 2), so the saved file must be filled paths only.

Drawing on strokes is easier and gives exact widths: use `stroke-width="2"`,
`stroke-linecap="round"`, `stroke-linejoin="round"`, `fill="none"`, on a 24 grid, with
coordinates on whole or half pixels so a 2 px stroke lands on the pixel grid. Then outline into a
scratch directory:

```bash
mkdir -p tmp/icon-draft/in tmp/icon-draft/out
# write tmp/icon-draft/in/<name>.svg
npx oslllo-svg-fixer -s tmp/icon-draft/in -d tmp/icon-draft/out
```

Write drafts under `tmp/`, never the repo root. After outlining:

- Union overlapping shapes into **one path per glyph where you can**. The font flattens all
  contours with the **nonzero** rule, so a shape overlapping another with opposite winding punches
  a hole. If the icon needs a strike-through or overlay that cannot be unioned, it is not a font
  icon — see "When the font is the wrong home" in the docs.
- Set `fill="currentColor"`, keep `fill-rule="evenodd"` only where a path needs a hole.
- Strip `clip-path`, `mask`, `filter`, `<style>`, `<g transform>`, and any `stroke` attribute.

## 4. Verify before it goes in the font

Render the draft next to two neighbours at 24 px and at 16 px, and look at it:

```bash
# magick is available in the Docker image; on Windows use any SVG-to-PNG route that works
magick -background white -density 600 tmp/icon-draft/out/<name>.svg -resize 96x96 tmp/icon-draft/<name>.png
```

Compare against the neighbours in one image. Reject the draft if the line looks thinner or
heavier, the terminals are square, the glyph is solid where its neighbours are outlined, it
sits off-centre, or it turns to mush at 16 px. Fix and re-render until it does not stand out.

## 5. Add it to the icon font

1. Name it in kebab-case after what it **depicts**, not the feature using it (`sessions.svg`,
   not `settings-sessions.svg`); no `-01` style suffixes. The file name becomes the class:
   `marker-pin.svg` → `.icon-marker-pin`.
2. Save to `src/nodejs/icons/<name>.svg`.
3. Regenerate: `./icons-to-font.cmd` (= `npm run font`). Rewritten files:
   `src/nodejs/fonts/svgtofont/icon.css` and `icon.woff2`. Codepoints of other icons shift when a
   name sorts in between — expected, and the CSS and font always change together.
4. Check `git status`: the new `.svg`, `icon.css` and `icon.woff2` — nothing else. Any other
   changed file means `.svgtofontrc` or the tool version drifted; stop and look.
5. Use it: `<i class="icon-<name>"></i>`.

Commit the source and **all** regenerated files together, one commit. Do not edit `icon.css` or
`icon.woff2` by hand.

## Pitfalls

- A stroke-based SVG saved as-is comes out of the font as a solid silhouette. Always outline.
- On Windows `icon.woff2` can be locked by a running server loop or watcher, and regeneration
  or a later cherry-pick fails with `unable to unlink`. Stop the lock owner and retry.
- Icons are per-concept. Don't draw a new glyph for a feature when an existing one means the
  same thing — consistency beats novelty.
