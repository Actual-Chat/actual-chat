# Kitty SVGs

Open `index.html` directly in a browser to compare all eleven illustrations.
The gallery offers side-by-side, light-only and dark-only views, plus three preview sizes.
Its backgrounds come from `--background-01` in `src/nodejs/styles/colors.css`:
light `#FFFFFF`, dark `#28282E`.

## Editing and generation

`name.svg` is the editable light source. `name-dark.svg` and `index.html` are generated;
keep them with the sources so the gallery also works without a build or server.

From the repository root:

```sh
npm run images:kitties
npm run images:kitties:check
npm run test:svg-variants
```

The existing `build.mjs` asset-copy step also regenerates changed outputs before copying
the images. `--check` reports missing or stale outputs without writing anything.

Each light source includes a palette inside SVG metadata:

```xml
<metadata>
  <palette xmlns="urn:voxt:svg-themes" version="1">
    <color role="outline" light="#9997a4" dark="#575766"/>
    <color role="highlight" light="#ffffff" dark="#cfcfe5"/>
  </palette>
</metadata>
```

This abbreviated example shows the format; actual palettes map every literal paint color.
Colors use three- or six-digit RGB hex. Opacity is authored separately and preserved.
The `role` label describes intent. Optional `part="ear-right"` scopes a rule to a
`data-part` group and its descendants; `target="asset--backdrop-gradient"` scopes it to an
element ID, including a gradient and its stops. A nearer scope wins, with an ID override
ahead of a part override on the same element. Global rules are the fallback.

The generator uses SVGO's XML parser with only the palette plugin enabled, plus PostCSS
for paint declarations in styles. It remaps fills, strokes, gradient/filter colors and
CSS paint declarations, then removes the palette metadata from the dark output.
Missing colors, unknown scopes, duplicate rules and unsupported schema versions fail generation.
Palette colors in `<style>` blocks use global rules; use attributes or inline styles for scoped exceptions.

## Animation

IDs, `data-part`, `data-pivot`, transforms, paths, gradient geometry and CSS motion keyframes
survive generation. Add animation to the light source, then regenerate its dark sibling.
Keep palette metadata in the editable source when using other SVG tools.

The sleeping, error and loading cats keep their fill-inset clips inside each motion group,
so the clipping follows the part when it moves. Keep white markings joined internally;
insetting each white patch separately can introduce visible seams.

Five sources include self-contained CSS animations that also run through ordinary, cacheable
`<img>` elements. Both themes use the same timing and geometry.

| Source | Loop | Motion |
| --- | --- | --- |
| `empty-chat.svg` | 5 seconds | Fly approaches the image-right ear; ear flicks twice. Extended hind leg twitches with a 2.5-second offset. |
| `error-cat.svg` | 5 seconds | Two quick mouse taps in a row, each completing in 0.275 seconds. The pair starts at 3.5 seconds; the full loop remains 5 seconds. |
| `invite-cat.svg` | 10 seconds | Grounded robot flicks its image-left ear at 1.3–1.625 seconds; all three flying robots respond together at 1.775–2.075 seconds. |
| `loading-cat.svg` | 8 seconds | Tail sways and head tilts slightly upright, then returns. The nearby star now occupies the lower-left pane. |
| `phone-verification-cat.svg` | 8 seconds | Smile moves left, center, right, center in 0.25-second moves, holding 0.5 seconds at each side and 3 seconds at center. Nose-to-lip connector is 15% longer; screen keeps its fixed blue gradient. |

All five honor `prefers-reduced-motion: reduce`: moving parts stay in their resting poses
and the sleeping cat's fly is hidden. Animation styles, pivots and grouped parts live in
the light source; regenerate after editing instead of changing the dark file directly.
The sleeping cat's `hind-leg-right` group is separate from its two front paw groups.

Animated ears keep their base outlines with the stationary head. Only their fills and
upper edges rotate; the head paints over the ear underlap. Preserve that draw order when editing.
Invite's grounded ear turns counterclockwise and the three flying ears turn clockwise
around their upper head attachments.
In the invite SVG, ear IDs name image sides: the flying cats' anatomical left ears
use the image-right `ear-right` groups.

Parent-controlled playback needs an inline SVG because `<img>` does not expose its
internal elements to the page. Runtime component migration is separate from these assets.

The app's effective `Theme.currentTheme` should select the image URL during runtime integration.
Use the dark sibling for `dark`; the approved light source also suits the light-background `ash` theme.
