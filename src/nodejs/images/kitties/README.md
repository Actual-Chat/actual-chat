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

These assets are currently static. Porting the existing sleeping-cat sequence requires
separating its extended hind leg and adding the fly; the existing paw groups are the front paws.
Use the new viewBox coordinates for pivots and movement distances, and include reduced-motion handling.
Timed CSS animation can live inside SVGs used through `<img>`. Parent-controlled animation
needs an inline SVG because `<img>` does not expose its internal elements to the page.

The app's effective `Theme.currentTheme` should select the image URL during runtime integration.
Use the dark sibling for `dark`; the approved light source also suits the light-background `ash` theme.
