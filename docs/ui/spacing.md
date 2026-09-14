# Spacing: inset, gap, and why margins are rare

This document is the rule set for **space between and inside UI components** —
padding, margin, and flex/grid `gap`. It exists because inconsistent,
ad-hoc spacing is the single most common source of visual drift in the app: the
modal audit found one shared class (`.dialog-body`) overridden in ~12 different
ways across 22 files, purely because no rule said *who owns the space*.

The short version: **a component owns the space _inside_ its border (its inset);
a container owns the space _between_ its children (the gap). Margins are the
exception, not the tool.**

## The three kinds of space

| | What it is | Who owns it |
|---|---|---|
| **Padding (inset)** | Space between a component's border and its content. | The **component**. Only meaningful when the element has a background, border, or is itself a visual surface. |
| **Gap** | Space between the children of a `flex`/`grid` container. | The **parent container**. |
| **Margin** | Space *outside* a component, pushing neighbours away. | Nobody should — see below. |

## The golden rule

> A reusable component owns what is **inside** its boundary (its inset) and does
> **not** set an outer margin. Space **between** components is the parent's
> responsibility, expressed as `gap` on the container.

An outer margin on a reusable component is a defect: it reaches out and changes
the layout of whatever sits next to it, so the same component spaces itself
differently depending on where it lands. Two buttons in a row, three form
blocks in a column, header → body → footer in a modal — none of them should
carry margins. The container that stacks them sets a `gap`, and every child
inherits the same, correct spacing for free.

::: warning Margin is not banned, it is rare
Legitimate uses exist — a single self-nudging element (`-mt-2` to overlap a
divider), optical alignment tweaks, `margin: auto` for centering/pushing in a
flex row. If you reach for margin to create the gap *between two components*,
stop: that gap belongs to their container.
:::

## The vocabulary

These six primitives (from Nathan Curtis / EightShapes, *Space in Design
Systems*) cover essentially every spacing rule we write. Name the intent, then
the CSS follows:

| Primitive | Shape | Typical use |
|---|---|---|
| **inset** | Equal padding all around | A card / panel body |
| **inset-squish** | Less vertical than horizontal | Buttons, toolbars, **action/footer bars** |
| **inset-stretch** | More vertical than horizontal | Tall hero blocks |
| **stack** | Vertical space between stacked items | header → body → footer, a column of form blocks |
| **inline** | Horizontal space between items in a row | Buttons next to each other, an icon + label |
| **grid** | Gutters between rows/columns | Tile grids, media galleries |

`stack` and `inline` are almost always a container `gap`, never per-child
margins. `inset*` is the component's own padding.

## The scale

All spacing is a multiple of the **4px** base, which is exactly the Tailwind
scale we already use — prefer a scale step over an arbitrary value:

| Token | `1` | `2` | `3` | `4` | `6` | `8` |
|---|---|---|---|---|---|---|
| Value | 4px | 8px | 12px | 16px | 24px | 32px |

`gap-y-2` = 8px, `p-4` = 16px, and so on. If you find yourself writing a raw
`padding: 6px`, round to a scale step.

## Applying it: the modal, worked example

A modal is a **stack** of regions. Model it exactly as the primitives say:

```
.modal-frame            ← STACK container: owns ONLY the vertical gap between regions
├─ .modal-header        ← region, owns its inset
├─ .dialog-body         ← region, owns its inset
└─ .dialog-footer       ← region (inset-squish), owns its inset
   └─ .dialog-buttons   ← INLINE group inside the footer: gap between buttons
```

The rules that fall out of this:

1. **The frame owns the gap, nothing else.** `.modal-frame` sets `gap-y-2`
   (the one, uniform separation between header/body/footer). It does **not**
   set `padding-bottom`, side padding, or per-region spacing.
2. **Each region owns its inset.** The body's content padding, the footer's
   `inset-squish`, the header's horizontal inset — each lives on that region,
   not on the frame and not leaked in from a sibling.
3. **Buttons live *inside* the footer.** They are an `inline` group (`gap-x`
   between them), not a sibling of the footer. The footer is the component; the
   buttons are its content.
4. **The last region owns the bottom inset — including the safe area.** This is
   the subtle one:

   ```css
   /* footer present → the footer owns the bottom inset */
   .dialog-footer {
       padding-bottom: calc(1rem + var(--safe-area-bottom));
   }
   /* no footer → the body is last, so it owns the bottom inset */
   .modal-frame:not(:has(.dialog-footer)) .dialog-body {
       padding-bottom: calc(1rem + var(--safe-area-bottom));
   }
   ```

   The frame never carries `padding-bottom`. When there is no footer the bottom
   space does not vanish — it becomes the body's inset. This is why "the modal
   has padding only at the bottom while the buttons have none" is wrong: the
   bottom inset belonged to a sibling (the frame) instead of to the region that
   actually sits at that edge.

::: warning Bottom inset on a scrolling body
`.dialog-body` scrolls (`overflow-y-auto`), and modern browsers honour its
`padding-bottom` — short content trails, long content scrolls its last item
clear of the home indicator. **But a `VirtualList` body is the exception**:
padding on the scroll container is unreliable there (see the tail-clip note in
[the virtual list spec](./virtual-list.md)). Give those a trailing spacer / last
item that carries the inset instead of a container `padding-bottom`.
:::

The same reasoning generalizes: whichever region sits at an edge of the frame
owns the inset at that edge. The frame is a pure stack container.

## Safe areas are an inset, not a margin

The bottom safe area folds into the *last region's* inset exactly as above; the
frame does not stop short of it. This is the same principle
[Safe areas](./safe-areas.md) states from the device side: *an inset is not a
margin the app stops at — a surface occupies the inset and pads its content
inward.*

## Review checklist

Before adding a spacing declaration, check:

- [ ] Am I creating space **between** two components? → It belongs to their
  container as `gap`, not to either child as margin.
- [ ] Am I creating space **inside** a component with a surface (bg/border)? →
  `padding` on that component is right.
- [ ] Is this a `margin` on a reusable component? → Justify it (self-nudge,
  optical, `auto`); otherwise it is the wrong tool.
- [ ] Is the value a **scale step** (multiple of 4px)? → If not, round.
- [ ] Does an existing shared class already own this space? → Don't re-declare
  it per feature; fix or extend the shared rule.
- [ ] For an edge that meets the screen, does the **region at that edge** own
  the safe-area inset (not an outer wrapper)?

## Related

- [Component guidelines](./components.md) — file structure, CSS naming, the
  `@apply` grouping rules.
- [Safe areas](./safe-areas.md) — the device-inset side of the same principle.
- [Coding style](../CODING_STYLE.md) — the CSS conventions this builds on.
