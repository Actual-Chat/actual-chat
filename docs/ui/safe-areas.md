# Safe Area Support

The app uses CSS-driven safe areas via `viewport-fit=cover` and `env(safe-area-inset-*)`. Background layers may extend into safe areas, but interactive/content elements are pushed inward by the inset amounts.

## The rule

An inset is not a margin the app stops at. A panel **occupies** the inset — what
changes is only what it paints there.

| | Top | Bottom |
|---|---|---|
| **Narrow** | The panel's own background continues into the inset, **including a background image**: a blurred place wallpaper reaches the very top of the screen, behind the clock and battery. | The panel continues into the inset too, under the dissolve — a gradient plus blur, so the system navigation stays legible over it. |
| **Wide** | One flat strip across the **whole** width, in the navbar's left-side colour. Panels do **not** each extend their own background here. | Same as narrow: the panel continues, under the dissolve. |

Interactive content is still pushed inward by the inset in every case. Only the
*background* extends.

::: warning A spacer is not an extension
`<div class="safe-area-top">` reserves the inset and paints nothing, so whatever
sits behind it shows through. That is fine where the ancestor's flat colour is
already the intended background, and wrong wherever the component has a
background of its own — an image, a gradient, a different surface — because the
background then stops at the spacer and leaves a visible band above it.

To extend a background, grow the element and pad it:

```css
.some-header {
    height: calc(6.5rem + var(--safe-area-top));
    padding-top: var(--safe-area-top);
}
```
:::

## CSS Variables

Four CSS custom properties wrap the native `env()` values, defined on `:root` in `main.css`:

```css
:root {
    --safe-area-top: env(safe-area-inset-top, 0px);
    --safe-area-bottom: env(safe-area-inset-bottom, 0px);
    --safe-area-left: env(safe-area-inset-left, 0px);
    --safe-area-right: env(safe-area-inset-right, 0px);
}
```

All safe area handling throughout the app references these variables rather than calling `env()` directly. This enables debug overrides via body classes.

## Testing Safe Areas

On desktop browsers, `env(safe-area-inset-*)` resolves to `0px`, so safe areas are invisible by default. Two methods let you simulate them:

### Method 1: `debugUI` (runtime, after JS loads)

Open the browser console and run:

```js
debugUI.showSafeAreas(true)   // Force all 4 insets to 34px
debugUI.showSafeAreas(false)  // Force all 4 insets to 0px
debugUI.showSafeAreas(null)   // Reset to real env() values
```

This adds/removes `show-safe-areas` or `hide-safe-areas` classes on `<body>`, which override the CSS variables. The change takes effect immediately and persists until the page is reloaded.

**Use this for:** testing the app after it has fully loaded — navigating between pages, opening dialogs, menus, panels, etc.

**Shortcut:** the `Ctrl+Shift+L`, `S` chord (`⌘+⇧+L`, `S` on macOS) flips between forced 34px insets and the real `env()` values. It's registered only on development instances (`HostInfo.IsDevelopmentInstance`) and is deliberately omitted from the `Ctrl+/` keyboard shortcuts dialog. See `AlwaysVisibleComponents.razor` and `DebugUI.toggleSafeAreas`.

### Method 2: CSS override in `main.css` (compile-time, before JS loads)

Uncomment the block near the top of `src/nodejs/styles/main.css`:

```css
/* Uncomment to force safe areas before JS loads (skeleton testing, etc.) */
body {
    --safe-area-top: 34px;
    --safe-area-bottom: 34px;
    --safe-area-left: 34px;
    --safe-area-right: 34px;
}
```

This forces safe areas from the very first paint, before any JavaScript runs. The `debugUI.showSafeAreas()` and `body.show-safe-areas` class will override it once JS kicks in.

**Use this for:** testing the loading skeleton (`splash-page-skeleton`) and the initial render before Blazor starts. Remember to comment it back out when done.

### What to check

When safe areas are active, verify:

1. **Left/right strips** — dark opaque bars at screen edges, always visible on top of everything
2. **Layout header** — top padding pushes chat title / buttons down; header background extends into the safe area
3. **Layout footer** — bottom padding on the editor or container; no content hidden behind the bottom inset
4. **Left panel** — top spacer pushes Voxt icon down; bottom blur overlay visible over the chat list
5. **Right panel** — header background image extends into top safe area; content buttons offset downward; bottom blur overlay visible
6. **Dialogs** — stretch modals have top/bottom padding; bottom-sheet modals have bottom padding; full-screen chrome modals have all 4
7. **Menus** — positioned inward from left/right/bottom edges
8. **Toasts** — offset from the top (narrow) or bottom (wide) by the safe area
9. **Landing page** — header offset from top and sides; download section has bottom padding
10. **Loading skeleton** — all 4 sides padded with `bg-03` background visible in safe area regions

## CSS Classes

### Spacer Elements

Inserted as `<div>` elements at component edges to push content inward:

| Class | Purpose | Size |
|-------|---------|------|
| `.safe-area-top` | Vertical spacer at top | `height: var(--safe-area-top)` |
| `.safe-area-bottom` | Vertical spacer at bottom | `height: var(--safe-area-bottom)` |
| `.safe-area-left` | Horizontal spacer at left | `width: var(--safe-area-left)` |
| `.safe-area-right` | Horizontal spacer at right | `width: var(--safe-area-right)` |

All spacers have `flex-shrink: 0` so they don't collapse in flex containers.

**Colors:**

| Direction | Background | Reason |
|-----------|-----------|--------|
| Left, right | `var(--background-04)` (dark, opaque) | Always visible as permanent strips at screen edges. The `::after` pseudo-elements on `.safe-area-left` / `.safe-area-right` use `position: fixed; z-index: 99999` to paint over everything, ensuring these strips are visible regardless of scroll position or overlay state. |
| Top (narrow), bottom | `transparent` | Not standalone coloured strips: each component extends its own background into the inset via `padding-top` / `padding-bottom` on the appropriate element (header extends `bg-01`, editor extends `bg-post-panel`, a place header extends its wallpaper). See [The rule](#the-rule). |
| Top (wide) | navbar left-side colour | One flat strip across the whole width. Panels do **not** extend their own backgrounds into the top inset in wide mode. |

The bottom safe area in scrollable panels (left panel chat list, right panel content) uses `.safe-area-bottom-overlay` which is transparent with `backdrop-filter: blur(8px)`, blurring content that scrolls underneath.

### Overlay Element

| Class | Purpose |
|-------|---------|
| `.safe-area-bottom-overlay` | Absolute-positioned bottom overlay with `backdrop-filter: blur(8px)`. Used in scrollable panels (left panel chat list, right panel content) to blur content scrolling under the bottom safe area. |

### Marker Class

| Class | Purpose |
|-------|---------|
| `.has-safe-area-bottom` | Added to elements that handle their own bottom safe area padding (e.g., `ChatMessageEditor`). Parent containers use `:not(:has(.has-safe-area-bottom))` to skip their own bottom padding when a child already handles it. |

---

## Component Categories

### 1. Base Layout (left/right edges — always active)

**File:** `BaseLayout.razor`, `main.css`

The outermost app layout places `.safe-area-left` and `.safe-area-right` divs flanking the main content:

```
<div class="base-layout">
    <div class="safe-area-left"></div>    ← fixed left strip
    <div class="base-layout-body">...</div>
    <div class="safe-area-right"></div>   ← fixed right strip
</div>
```

These are always present in both narrow and wide modes. The `::after` pseudo-elements ensure the colored strips are fixed to the screen edges. Background color: `var(--background-04)` (dark).

Left/right safe areas are **only** handled here at the top level — no inner components duplicate them, except for full-screen overlays that cover the entire viewport.

### 2. Middle Panel Layouts (top/bottom edges)

**Files:** `main.css`, `PageWithHeaderAndFooter.razor`

Two layouts share the same structure: `default-layout` and `list-view-layout`.

**Header — top safe area:**

```css
.layout-header {
    padding-top: var(--safe-area-top);
    min-height: var(--safe-area-top);
}
```

The header pushes its content (chat title, buttons) down by the top inset. When collapsed/empty, `min-height` ensures the safe area strip is still visible.

For `list-view-layout`, all `max-height` values on `.layout-header` include `var(--safe-area-top)` in their `calc()` expressions (e.g., `calc(3.5rem + var(--safe-area-top))`).

**Footer — bottom safe area via `.c-container`:**

```css
.page-with-header-and-footer .layout-body-wrapper > .c-container:not(:has(.has-safe-area-bottom)) {
    padding-bottom: var(--safe-area-bottom);
}
```

The `.c-container` (column-reverse flex parent of footer, subfooter, and body) adds bottom padding — but **only if** no child has `.has-safe-area-bottom`. When `ChatMessageEditor` is present, it has this class and handles its own bottom padding:

```css
.chat-message-editor {
    padding-bottom: var(--safe-area-bottom);
}
```

**Layout fix:** `.default-layout` must have `@apply flex-y h-full` to establish a proper flex column chain. Without this, `h-full` on nested flex items causes them to overflow and push the footer off-screen.

### 3. Side Navigation Panels

**File:** `side-nav.css`

On narrow screens, side panels are `position: fixed` and cover the full viewport. Their position is offset by safe areas:

```css
.side-nav {
    left: var(--safe-area-left);
    right: var(--safe-area-right);
    width: auto;
}
```

On wide (`md+`), side panels are `position: relative` with `left: 0; right: auto; width: full`.

### 4. Left Panel

**Files:** `LeftPanelButtons.razor`, `LeftPanelContent.razor`, `left-chat-search-input.css`

**Left panel buttons** (thin sidebar with icons):
- `<div class="safe-area-top">` at top of `.c-content` — pushes the Voxt icon and group buttons down
- `<div class="safe-area-bottom">` at bottom — pushes the account dropdown up

**Left panel content** (chat list area):
- `<div class="safe-area-top">` at top — pushes the header and tab bar down
- `<div class="safe-area-bottom safe-area-bottom-overlay">` at bottom of the scrollable list — blurs content scrolling under the bottom inset, positioned absolute so it doesn't affect scroll

**Search input:** positioned with `top: var(--safe-area-top)` to align below the safe area.

**Create chat/place FAB** (`.chat-list-fab`, bottom-right of the chat list): `bottom: calc(1rem + var(--safe-area-bottom))`. When the Active chats band is present the FAB switches to `bottom: 100%` relative to that band, whose own `padding-bottom: calc(0.5rem + var(--safe-area-bottom))` already clears the inset. The right inset needs no handling here — the panel's right edge is already inset by `.side-nav` (narrow) or the base layout's `.safe-area-right` strip (wide).

### 5. Right Panel

**Files:** `right-panel.css`, `RightPanelContent.razor`

**Header** — the background image extends into the top safe area while interactive content is pushed down:

```css
.right-panel > .c-header > .c-top {
    height: calc(6.5rem + var(--safe-area-top));
    padding-top: var(--safe-area-top);
}
.right-panel > .c-header > .c-center {
    top: calc(4.5rem + var(--safe-area-top));
}
.right-panel > .c-header > .c-buttons {
    top: calc(0.5rem + var(--safe-area-top));
}
```

**Content** — uses the blur overlay at the bottom of scrollable content:
- `<div class="safe-area-bottom safe-area-bottom-overlay">` at the bottom of `.c-panel-content`

### 6. Full-Screen Dialogs (Narrow Stretch Modals)

**Files:** `modal.css`, individual dialog CSS files

The modal overlay on narrow adds left/right padding:

```css
body.narrow .modal-overlay {
    padding-left: var(--safe-area-left);
    padding-right: var(--safe-area-right);
}
```

Full-screen chrome modals get all 4 edges:

```css
.modal-overlay-fullscreen .modal-frame.modal-chrome {
    padding-top: var(--safe-area-top);
    padding-bottom: var(--safe-area-bottom);
    padding-left: var(--safe-area-left);
    padding-right: var(--safe-area-right);
}
```

Individual stretch dialogs add top/bottom padding on their `narrow-view__modal__position-stretch` class:

| Dialog | CSS File |
|--------|----------|
| Settings | `settings-modal.css` |
| Onboarding | `onboarding-modal.css` |
| Sign In | `sign-in-modal.css` |
| Share | `share-modal.css` |
| Add Member | `add-member-modal.css` |
| Forward Message | `forward-message-modal.css` |
| New Chat | `new-chat-modal.css` |
| New Place | `new-place-modal.css` |
| Chat Settings | `chat-settings-modal.css` |
| Own Avatar Editor | `own-avatar.css` |
| Download App | `download-app-modal.css` |
| Time Zone Editor | `time-zone-editor-modal.css` |
| Photo Troubleshooter | `photo-troubleshooter-modal.css` |
| Recording Troubleshooter | `guides.css` |
| Incoming Share | `incoming-share-modal.css` |
| Premium Features | `landing.css` |

Pattern for each:

```css
body.narrow .xxx-modal.narrow-view__modal__position-stretch {
    padding-top: var(--safe-area-top);
    padding-bottom: var(--safe-area-bottom);
}
```

### 7. Bottom-Sheet Modals

**File:** `modal.css`

Bottom-sheet modals (`modal-md`) on narrow get bottom padding only:

```css
body.narrow .modal-frame.modal-md {
    padding-bottom: var(--safe-area-bottom);
}
.narrow .narrow-view__modal__position-bottom {
    padding-bottom: var(--safe-area-bottom);
}
```

Small centered modals (`modal-sm`) don't touch edges and need no safe area handling.

### 8. Menus

**File:** `menu.css`

Menus on narrow are positioned inward from safe areas:

```css
body.narrow .ac-menu {
    left: calc(1rem + var(--safe-area-left)) !important;
    right: calc(1rem + var(--safe-area-right));
    bottom: calc(2.5rem + var(--safe-area-bottom));
}
```

### 9. Toasts

**File:** `toast.css`

Toast positioning is offset by safe areas:

```css
body.narrow .toast-container {
    left: var(--safe-area-left);
    right: var(--safe-area-right);
    top: calc(3rem + var(--safe-area-top));
}
body.wide .toast-container {
    bottom: calc(3rem + var(--safe-area-bottom));
}
```

### 10. Media Viewer

**File:** `visual-media-viewer-modal.css`

The image/video viewer uses safe area padding for its header and footer bars, plus `translateY` animations that account for safe area offsets:

```css
/* Header */
padding-top: var(--safe-area-top);
transform: translateY(calc(-3.5rem - var(--safe-area-top)));  /* hide animation */

/* Footer */
padding-bottom: var(--safe-area-bottom);
transform: translateY(calc(3.5rem + var(--safe-area-bottom)));  /* hide animation */
```

### 11. Video Panel (Expanded)

**File:** `video-panel.css`

When expanded to full-screen, the video panel adds all 4 safe area paddings:

```css
.video-panel.expanded {
    padding: var(--safe-area-top) var(--safe-area-right) var(--safe-area-bottom) var(--safe-area-left);
}
```

### 12. Landing Page

**File:** `landing.css`

**Header** — fixed at top, offset by safe areas:

```css
.landing .landing-header {
    left: var(--safe-area-left);
    right: var(--safe-area-right);
    padding-top: calc(0.5rem + var(--safe-area-top));
    height: calc(4rem + var(--safe-area-top));
}
```

**Docs layout** — uses `::before` and `::after` pseudo-elements for top/bottom safe area with `backdrop-filter: blur(8px)`:

```css
.docs-layout::before {
    height: var(--safe-area-top);
    backdrop-filter: blur(8px);
}
.docs-layout::after {
    height: var(--safe-area-bottom);
    backdrop-filter: blur(8px);
}
```

**Page links** (download section) — bottom padding:

```css
.landing .page-links {
    padding-bottom: var(--safe-area-bottom);
}
```

**Cookie settings** — positioned inward from all edges:

```css
.cookie-settings {
    bottom: calc(1rem + var(--safe-area-bottom));
    left: calc(1rem + var(--safe-area-left));
    right: calc(1rem + var(--safe-area-right));
}
```

### 13. Web Splash (Loading Screen)

**File:** `web-splash.css`

The progress bar is offset from the bottom safe area:

```css
.web-splash .progress {
    bottom: calc(32px + var(--safe-area-bottom));
}
```

### 14. Loading Skeleton

**Files:** `skeleton.css`, `splash-page-skeleton.lit.ts`

The splash page skeleton wraps all content with safe area padding and uses `overflow-hidden` to clip content within the safe area:

```css
splash-page-skeleton {
    @apply bg-03 overflow-hidden;
    padding-top: var(--safe-area-top);
    padding-bottom: var(--safe-area-bottom);
    padding-left: var(--safe-area-left);
    padding-right: var(--safe-area-right);
}
```

On narrow, the left panel skeleton's `.side-nav` positioning is overridden to `relative` so it respects the parent's padding instead of being fixed to the viewport:

```css
body.narrow splash-page-skeleton .left-panel-skeleton.side-nav {
    @apply relative;
    @apply top-auto bottom-auto left-auto right-auto;
    @apply w-full z-auto;
}
```

---

## Architecture Summary

| Layer | Handles | Mechanism |
|-------|---------|-----------|
| **Base layout** | Left, right (always) | `.safe-area-left` / `.safe-area-right` divs with fixed `::after` overlays |
| **Layout header** | Top | `padding-top` + `min-height` on `.layout-header` |
| **Layout container** | Bottom (conditional) | `padding-bottom` on `.c-container:not(:has(.has-safe-area-bottom))` |
| **Chat editor** | Bottom (when present) | `padding-bottom` on `.chat-message-editor.has-safe-area-bottom` |
| **Side panels** | Left/right offset | `left` / `right` on `.side-nav` |
| **Left panel** | Top, bottom | `.safe-area-top` divs + `.safe-area-bottom-overlay` blur |
| **Right panel** | Top, bottom | CSS `calc()` offsets on header + `.safe-area-bottom-overlay` blur |
| **Modals (stretch)** | Top, bottom, left, right | `padding` on modal frame/overlay |
| **Modals (bottom-sheet)** | Bottom | `padding-bottom` on `.modal-frame.modal-md` |
| **Menus** | Left, right, bottom | `calc()` offsets on position properties |
| **Toasts** | All relevant edges | `calc()` offsets on position properties |
| **Landing** | All edges | Various — header offset, pseudo-elements for docs, padding for page-links |
| **Skeleton** | All 4 edges | `padding` on `splash-page-skeleton` |
| **Video panel** | All 4 edges (expanded) | `padding` shorthand |
| **Media viewer** | Top, bottom | `padding` + `translateY` in animations |

## Key Design Decisions

1. **CSS variables over `env()` direct usage** — Enables debug overrides without touching every component.
2. **Left/right always at base layout** — The dark strips are always visible; inner components never duplicate them (except full-viewport overlays).
3. **`.has-safe-area-bottom` pattern** — Avoids double bottom padding when the chat editor is present vs. when viewing a read-only chat or other page.
4. **Blur overlays for scrollable lists** — The bottom safe area in scrollable panels (left chat list, right panel content) uses `position: absolute` + `backdrop-filter: blur(8px)` so content scrolls underneath while the safe area is visually distinct.
5. **Skeleton safe areas in CSS only** — The loading skeleton uses CSS padding rather than JS, so safe areas are visible before any JavaScript loads.
6. **Panels occupy the insets, they don't stop at them** — see [The rule](#the-rule). The visible test is a place header on a notched phone: its blurred wallpaper must reach the top of the screen, not stop below the clock. Wide mode is the exception, and deliberately so — a single navbar-coloured strip reads as one window chrome rather than as several panels each bleeding upward.
