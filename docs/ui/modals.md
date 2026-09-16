# Modals

Every modal in the app is one component — `DialogFrame` — configured by a small
set of enums, not by ad-hoc class strings. This document is the target model the
modal refactor converges on. It builds directly on
[Spacing: inset, gap, margins](./spacing.md) (who owns the space) and
[Safe areas](./safe-areas.md) (the device insets).

The rule in one line: **a modal is three stacked regions — header, body, footer —
inside a frame that owns only the gap between them. Buttons live inside the
footer. The region at each edge owns that edge's inset (including the safe area).**

## Anatomy — three regions

```
.modal-frame              ← STACK container: owns ONLY the gap between regions
├─ .modal-header          ← region, owns its inset (or omitted)
├─ .dialog-body           ← region, scrolls, owns its content inset
└─ .dialog-footer         ← region, owns its bottom inset + safe area
   ├─ (optional content)  ← e.g. the Share comment input
   └─ .dialog-buttons     ← the action row, INSIDE the footer
```

- `header` / `footer` are `flex-none`; `body` is `flex-1 overflow-y-auto`. Only
  the body scrolls; header and footer stay put.
- The frame sets one uniform `gap` between regions and **no** `padding-bottom` —
  see [spacing.md](./spacing.md) for why the last region owns the bottom inset.
- **Buttons are nested inside the footer**, not a sibling of it. In `DialogFrame`
  the `Buttons` slot renders inside the `.dialog-footer` element, so existing
  callers keep both `Footer` and `Buttons` slots without any migration.

## Presentation — how the modal sits (mobile)

`enum Presentation { Fullscreen, Docked, Floating }`. This is the **mobile
(narrow)** placement. On **desktop (wide)** all three collapse to a centered
floating card whose width comes from `Size` (below).

| Presentation | Narrow (mobile) | Wide (desktop) | Typical use |
|---|---|---|---|
| **Fullscreen** | Edge-to-edge, fills the screen; header owns the top safe area | Centered card (`Size`) | Settings, media viewer, big forms |
| **Docked** | Full-width sheet glued to the bottom edge, rounded top, swipe-down to dismiss | Centered card (`Size`) | Pickers, action sheets |
| **Floating** | Smaller sheet floating above the home indicator, rounded all round | Centered card (`Size`) | Confirms, short forms |

Replaces today's scattered `narrow-view__modal__position-stretch` /
`-bottom` classes and the `modal-sm` float.

## Size — desktop width

`enum Size { Sm, Md, Lg }` → the centered card's width on wide (`md:w-*`).
Orthogonal to `Presentation`: a `Docked` picker and a `Floating` confirm can both
be `Md` on desktop. Replaces the `modal-sm` / `modal-md` / `fixed-height`
width strings.

## Header — HeaderMode

`enum HeaderMode { Default, Custom, None }` — a `[Parameter]` on `DialogFrame`,
paired with a `Header` render fragment for the `Custom` case.

| Mode | What | Notes |
|---|---|---|
| **Default** | Back arrow (inner steps) · title · close (×) | The standard `DialogHeader`. Back appears for wizard/dive-in inner steps (`ModalStepRef`). |
| **Custom** | The modal supplies its own header content (a decorative hero: wallpaper + avatar + actions, as in edit-chat / edit-place / own-avatar) via the `Header` fragment | Rendered in the **fixed header region** (`ModalFrame` renders `@Header` above the scrolling `.dialog-body`), so the hero **does not scroll** and its close (×) is always reachable. Because the hero sits outside the padded body, `BodyVariant` inset applies only to content — no bleed hacks. |
| **None** | No header at all | Emoji / GIF / media pickers. |

**Implementation status (2026-09-15).** `HeaderMode` + `Header` slot exist;
`DialogInteractiveHeader` and the submit-in-header plumbing are removed;
**OwnAvatar** uses `Custom` (fixed hero). **ChatSettings / PlaceSettings** are a
temporary exception — still hero-in-body (they are dive-in; hoisting the hero
into `DiveInModalPageContext.Header` needs care around render reactivity) and
will move to `Custom` in a follow-up.

## Body — BodyVariant

`enum BodyVariant { Default, Flush, Tiles }` — see [spacing.md](./spacing.md):

- **Default** — padded content (forms, text).
- **Flush** — `p-0`, edge-to-edge (lists, contact selector, map, image).
- **Tiles** — settings-style tiled rows.

This collapses the ~22 files that currently each re-declare `.dialog-body`
padding their own way.

## Footer — the action region

- **Buttons live inside the footer.** 1–3 buttons; full-width; `primary` /
  `cancel` / `danger` (destructive). Row on wide; on narrow they stack or go
  full-width. Built on the existing `DialogButtons` / `DialogButtonInfo`.
- The footer may also hold non-button content above the buttons (e.g. the Share
  comment input) via the `Footer` slot.
- The footer **owns the bottom inset including the safe area** (or the body does
  when there is no footer) — [spacing.md](./spacing.md).
- **No submit-in-header.** The `DialogInteractiveHeader` /
  `ForSubmitButton` / `ForFormSubmitButton` system has been removed; the primary
  action is always a footer button. The modals that used it (Forward, the guides,
  OwnAccount / OwnAvatar editors, Premium, NewChat / NewPlace) now keep their
  submit in the footer. `DialogFrameNarrowViewSettings` is reduced to
  `Position` + `ShouldHideButtons`.

## Keyboard on mobile

Removing submit-from-the-top only works if the footer stays reachable while the
on-screen keyboard is up. The rule (the cross-platform consensus for sheets with
inputs): **when the keyboard opens, do not pin the modal's bottom to the viewport
edge — pin it to the top of the keyboard** so the footer rises above it. The
`visualViewport` bottom / `has-viewport-height` handling the app already applies
on `body.device-ios` / `body.device-android` gives the reduced height; the footer
sits at its bottom, above the keyboard. Fullscreen resizes to that height; a
Docked/Floating sheet rises with the keyboard. This is the one point that **must
be validated on a real device** with the keyboard open before the
submit-in-header system is deleted.

## Safe areas

Per [safe-areas.md](./safe-areas.md) and [spacing.md](./spacing.md):

- **Top** — the header (Fullscreen) owns the top safe area; a headerless
  fullscreen modal's body does.
- **Bottom** — the footer owns it; with no footer, the body does. The frame
  never carries `padding-bottom`.

## Dismissal, gestures, transitions

- Overlay dim + tap-outside-to-close (except where a decision is required).
- **Docked** and **Floating** support swipe-down-to-dismiss on mobile.
- Enter/exit animation per presentation: Fullscreen and Docked slide up from the
  bottom; Floating fades / scales in. (Today's `.modal-overlay .modal-frame`
  opacity/transform is the starting point.)

## Anomalies to fold in

Four surfaces bypass `DialogFrame` today. Scope decisions for this refactor:

- **Settings**, **VisualMediaViewer** — on `ModalFrame`. **Out of scope — not
  touched now.** They keep their bespoke `ModalFrame` chrome; revisit later once
  the `DialogFrame` model has settled.
- **DemandUserInteraction** — bare `ModalChrome` with hand-rolled buttons.
  **Left as-is / must not regress** — it is deliberately minimal.
- **ChatQuickNav** — the ⌘K / Ctrl+K "jump to chat" command palette
  (`chat-quick-nav-modal`, opened via `ModalHost`): a search box + results list,
  not header/body/footer chrome. **Stays a documented exception** (it is a
  palette, not a dialog).

## Migration strategy

**Evolve `DialogFrame` in place — do not rewrite callers.**

1. Change `DialogFrame`'s render so the `Buttons` slot is nested inside
   `.dialog-footer`, and the frame drops `padding-bottom` (last region owns it).
   *No caller change; ~54 modals keep working.*
2. Add `Presentation`, `Size`, `HeaderMode`, `BodyVariant` parameters that map to
   classes, replacing the magic strings. Migrate callers to the enums opportunistically.
3. Delete the interactive-header path; move the ~6 submit-in-header modals to
   footer buttons.
4. Formalise `HeaderMode.Custom`; convert edit-chat / edit-place off the
   hide-and-overlay hack.
5. Leave the anomalies alone this pass: Settings / VisualMediaViewer stay on
   `ModalFrame` (out of scope), DemandUserInteraction and ChatQuickNav untouched.

Each step is independently shippable; nothing is big-bang.

## Reuse

**Existing abstractions to reuse (not rebuild):** `DialogFrame`, `ModalFrame`,
`ModalChrome`, `DialogHeader`, `DialogInteractiveHeader` (to be removed),
`DialogButtons` / `DialogButtonInfo`, `DialogFrameNarrowViewSettings`,
`ModalStepRef` (steps), `FormBlock`, and `modal.css`. The refactor changes their
wiring and adds enums — it introduces no parallel modal component.

**New components:** only the enums (`Presentation`, `Size`, `HeaderMode`,
`BodyVariant`), which live in `UI.Blazor/Components/Modal/` beside `DialogFrame`.
They are modal-specific, so they stay in that folder rather than a shared project.

## Related

- [Spacing: inset, gap, margins](./spacing.md) — who owns the space; the modal
  worked example.
- [Safe areas](./safe-areas.md) — the device-inset rules the footer/header follow.
- [Component guidelines](./components.md) — file structure and CSS conventions.
