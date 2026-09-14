# Kitty SVGs

Open `index.html` directly in a browser to compare all eleven illustrations.
The gallery offers side-by-side, light-only and dark-only views, plus three preview sizes.
Its backgrounds come from `--background-01` in `src/nodejs/styles/colors.css`:
light `#FFFFFF`, dark `#28282E`.

## Files to edit

| File | Purpose |
| --- | --- |
| `name.svg` | Editable light original: geometry, palette metadata and animation. |
| `name-dark.svg` | Generated dark variant. Change its light original and regenerate. |
| [index.html](index.html) | Generated preview gallery; opens without a build or server. |
| [svg-preview.template.html](../../../../scripts/svg-preview.template.html) | Editable gallery layout and controls. |
| [generate-svg-variants.mjs](../../../../scripts/generate-svg-variants.mjs) | Palette remapping and gallery generation. |

Commit the light sources, generated dark siblings and any changed gallery output together.
All commands below run from the repository root with the project's npm dependencies installed.

## Editing and generation

1. Edit the relevant `name.svg` in a text or vector editor. Keep its `viewBox`, IDs,
   `data-*` attributes, palette metadata, definitions, motion groups and embedded `<style>`.
   Inspect the export diff: some editors strip metadata or flatten groups and transforms.
2. Keep the background transparent, the palette at eight distinct paint colors or fewer,
   and the outline a single color. Reuse existing colors where possible. Use native SVG
   gradients, with a palette rule for every stop color, instead of raster images or bands
   of tiny paths. Generation recolors gradients; it does not create them from traced bands.
3. Regenerate, check the outputs, and run the generator tests:

   ```sh
   npm run images:kitties
   npm run images:kitties:check
   npm run test:svg-variants
   ```

4. Reload `index.html` and inspect both themes at normal and large sizes. Watch a full
   animation cycle, including the most extreme pose, and check reduced motion. Inspect
   ears, paws, tail joins and light fills against the dark background.
5. Review the diff and commit the edited sources with their generated outputs.

`images:kitties` writes dark variants and the gallery; it does not modify the light originals.
`images:kitties:check` reports missing or stale outputs without writing anything.
`test:svg-variants` checks the remapping behavior, including preservation of animation CSS.
The existing [build.mjs](../../../../build.mjs) asset-copy step also regenerates changed outputs
before copying the images. The generator does not simplify or retrace geometry, so keep
source paths compact and avoid exporting raster content or excessive path points.

## Dark palette rules

To change only the dark appearance, edit the `dark` value in the light source's metadata.
To introduce a new light paint, add or update its `light`/`dark` mapping too. Rules live in
structured SVG metadata, not comments. Keep exactly one version-1 palette per source:

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

For example, these rules keep one part's white fill brighter and recolor a gradient's white
stop separately from the global white rule:

```xml
<color role="highlight" light="#ffffff" dark="#e5e5f2" part="ear-right"/>
<color role="backdrop" light="#ffffff" dark="#303038" target="asset--backdrop-gradient"/>
```

Place scoped rules inside the same palette as the global rules, using existing `data-part`
values or IDs from that SVG. A `part` rule matches every group with that `data-part` value;
use an ID rule to distinguish individual elements. To recolor a shared gradient separately,
copy its definition under a unique ID, update the relevant `url(#...)` reference and target
the new gradient ID. A rule on the shape using a gradient does not reach its definition in
`<defs>`.

Use an identity mapping (`light` and `dark` set to the same hex color) to retain a color in
both themes. Keep alpha in `opacity`, `fill-opacity`, `stroke-opacity` or `stop-opacity`;
named colors, `rgb(...)` and eight-digit hex paints are not supported by the palette parser.

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

Keep each moving part's fill, markings, outline and clips together so none remain behind
when it moves. Match CSS `transform-origin` to the intended pivot in the group's coordinate
system; `data-pivot` records that point but does not drive the animation. Preserve existing
parent transforms and `transform-box: view-box` when adjusting an ear or other joint.
Use asset-specific class and keyframe names, and retain the reduced-motion resting pose.

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

## Adding or renaming an illustration

Add an editable SVG with a lowercase, hyphenated name, such as `new-cat.svg`, and a complete
palette. The generator discovers all `.svg` files here except names ending in `-dark.svg`;
it adds each source to the gallery automatically. Prefix IDs with the asset name and update
all `url(#...)` references and animation selectors when copying an existing illustration.

Regenerate and review both themes. On rename or removal, also remove the old source and its
old generated dark sibling: the generator does not delete orphaned outputs. Update any app
references to the old filename separately.

## Common editing problems

| Symptom | Fix |
| --- | --- |
| `Unmapped paint` | Add a rule for that literal color in the light source's palette. Check gradient stops and CSS paints as well as paths. |
| `Unknown palette scope` | Correct the `part` or `target` to match an existing `data-part` or ID. Editors may have renamed or removed it. |
| Missing palette or unsupported version | Restore the source metadata; do not use a generated dark file as the editable original. |
| `Stale SVG outputs` | Run `npm run images:kitties`, then include the regenerated files in the commit. |
| Light rims show outside a dark outline | Inset or clip the underlying fill beneath the outline. Keep adjacent interior markings joined to avoid new seams. |
| A moving part leaves a line or gap behind | Check group membership, stationary duplicate paths, pivot coordinates and overlap at the joint throughout the cycle. |
| Browser still shows an old image | Reload the gallery and, if necessary, open the SVG directly and force-refresh it. |

Checks verify generated output and remapping behavior; visual review is still needed for
palette quality, geometry, seams and animation. The eight-color limit and compact path
geometry are authoring constraints, not enforced by the generation command.
