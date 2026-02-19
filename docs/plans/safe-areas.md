# Safe Area Support Plan

## Overview

Switch from native safe area handling to CSS-driven safe areas using `viewport-fit=cover` and `env(safe-area-inset-*)`. Background layers may extend into safe areas, but interactive/content elements must not. Edge-adjacent components add padding equal to the corresponding inset.

## CSS Classes

Define four directional classes and one transparency modifier:

```css
.safe-area-top    { height: env(safe-area-inset-top, 0px);    flex-shrink: 0; }
.safe-area-bottom { height: env(safe-area-inset-bottom, 0px); flex-shrink: 0; }
.safe-area-left   { width:  env(safe-area-inset-left, 0px);   flex-shrink: 0; }
.safe-area-right  { width:  env(safe-area-inset-right, 0px);  flex-shrink: 0; }

.safe-area-transparent { background: transparent; backdrop-filter: blur(8px); }
```

### Initial Step Styling

For the initial placement validation step, use simple debug-visible colors:
- **Left/right safe areas:** fully black background
- **Top/bottom safe areas:** `bg-04` background
- **Transparent safe areas:** transparent background + `backdrop-filter: blur(...)` so the safe area is visible but shows blurred content beneath it

Each class is rendered as a `<div>` inserted into components at the appropriate edge.

### Nesting Order

When a component needs both horizontal (left/right) and vertical (top/bottom) safe areas, horizontal safe areas take the outer position. The vertical safe areas fill the remaining space between them:

```
+-------+-------------------+--------+
| left  |    top            | right  |
|       +-------------------+        |
|       |                   |        |
|       |    content        |        |
|       |                   |        |
|       +-------------------+        |
|       |    bottom         |        |
+-------+-------------------+--------+
```

### Transparency

Overlays (modals, menus, bubbles) use `safe-area-transparent` because their safe area divs sit on top of a backdrop — the inset space should be see-through, not colored.

Panel headers/footers use colored safe area divs that match the panel's background, so the inset area looks like an extension of the panel.

---

## Detailed Placement Per Panel

### Left Panel

**Structure** (inside `SideNav.side-nav-left`):
```
SideNav (side-nav.side-nav-left)
└── div.left-panel (flex-x flex-1 h-full bg-04)
    ├── .left-panel-buttons (flex-none flex-x z-10 h-full bg-04)
    │   └── .c-content (flex-y items-end h-full)
    │       ├── NavbarButton.settings-btn (Voxt icon, top)
    │       ├── .c-buttons (flex-y grow) ← group/place buttons
    │       └── AccountDropdown ← account/settings button (bottom)
    │
    └── .left-panel-content (relative flex-y flex-1 w-full h-full bg-03)
        ├── LeftPanelContentHeader
        └── .left-panel-content-main-wrapper (flex-y grow overflow-hidden)
            └── .left-panel-content-main (flex-y grow overflow-hidden)
                └── NavbarContent
                    └── .navbar-group (flex-1 flex-y overflow-y-auto) ← SCROLLABLE CHAT LIST
```

**Top safe area:**
- Color: `bg-04` (matches the navigation sidebar background)
- Placement: inside `.left-panel-buttons > .c-content`, at the very top, pushing all buttons downward
- Also inside `.left-panel-content`, at the top, pushing header and list content down
- Both divs sit at the same vertical level (left-panel is `flex-x`)

**Bottom safe area:**
- The `.left-panel-buttons` side: a safe-area-bottom div inside `.c-content` at the bottom, pushing `AccountDropdown` upward. The safe area height shifts the account button up by `env(safe-area-inset-bottom)`.
- The `.left-panel-content` side: an overlay-style safe-area-bottom positioned at the bottom of `.left-panel-content-main`, covering the bottom of the scrollable chat list with backdrop blur. Uses `position: absolute; bottom: 0` with `pointer-events: none` so it doesn't block scrolling, but visually blurs the list items beneath it. The list should remain scrollable underneath.
- Transparent variant (backdrop blur) for the list overlay.

**Key files:**
- `UI.Blazor.App/Components/LeftPanel/LeftPanel.razor`
- `UI.Blazor.App/Components/LeftPanel/LeftPanelButtons.razor`
- `UI.Blazor.App/Components/LeftPanel/LeftPanelContent.razor`
- `UI.Blazor.App/Components/LeftPanel/left-panel.css`

---

### Right Panel

**Structure** (inside `SideNav.side-nav-right`):
```
SideNav (side-nav.side-nav-right)
└── div.right-panel (relative flex-y w-full h-full bg-03)
    ├── .c-header (relative flex-y items-center min-h-44)
    │   ├── .c-top (relative flex-none h-26 w-full) ← BACKGROUND IMAGE (blurred ChatIcon)
    │   ├── .c-center (absolute top-18 z-20 flex-x h-16 w-full) ← avatar + buttons
    │   └── .c-bottom (flex-y items-start w-full pt-10) ← title + description
    │
    └── .c-panel-content (flex-1 flex-y overflow-y-hidden)
        ├── .c-chat-info (flex-y gap-y-1 p-2 pt-4) ← notifications/summarize toggles
        └── .c-panel-tabs (flex-y h-full overflow-hidden) ← Members/Threads/Media tabs (scrollable)
```

**Top safe area — layered approach:**
- The background image (`.c-top` with blurred ChatIcon) should extend to the very top of the screen, behind the safe area. It occupies the full safe area + its normal height.
- The safe area pushes the interactive content (`.c-center` avatar/buttons, `.c-bottom` title) downward by `env(safe-area-inset-top)`.
- Implementation: The `.c-top` background image gets extra height via CSS (e.g. `padding-top: env(safe-area-inset-top)` or increasing height by the inset). The `.c-center` position (`top-18`) increases by the safe area inset. The `.c-bottom` padding adjusts accordingly.
- No blur needed at top — the background image provides the visual fill.

**Bottom safe area:**
- Similar to the left panel's content side: an overlay at the bottom of `.c-panel-content` or `.c-panel-tabs`.
- Pushes the tab content away from the bottom edge.
- Transparent with backdrop blur for the scrollable tab content.

**Key files:**
- `UI.Blazor.App/Components/RightPanel/RightPanel.razor`
- `UI.Blazor.App/Components/RightPanel/RightPanelHeader.razor`
- `UI.Blazor.App/Components/RightPanel/RightPanelContent.razor`
- `UI.Blazor.App/Components/RightPanel/right-panel.css`

---

### Middle Panel

**Structure:**
```
<div class="default-layout">  (or "list-view-layout")
    ├── .layout-header (z-50 flex-y flex-none w-full bg-01)
    │   ├── .c-content (ordinary-header) ← ChatHeader
    │   │   ├── back button
    │   │   ├── .chat-header-center (title, avatar, activity panel on wide)
    │   │   └── .chat-header-control-panel (video/translation buttons)
    │   └── .header-activity-panel-wrapper (narrow only, when listening)
    │       └── ChatActivityPanel (absolute z-10 bottom-0.5)
    │
    ├── .layout-subheader (sticky top-0 z-90)
    │
    ├── .layout-body-wrapper (flex-x justify-center flex-1 h-full overflow-hidden)
    │   └── .c-container
    │       ├── .layout-footer (sticky bottom-0 z-10 flex-y items-center)
    │       │   └── .writable-chat-footer (relative w-full bg-post-panel)
    │       │       └── ChatMessageEditor
    │       │           └── .post-panel (flex-x rounded-3xl bg-post-panel)
    │       ├── .layout-subfooter
    │       └── .layout-body (flex-y flex-1 overflow-y-auto) ← SCROLLABLE MESSAGE LIST
```

**Top safe area — part of header:**
- The safe area becomes part of `.layout-header`, making the header taller by `env(safe-area-inset-top)`.
- The header's background (`bg-01`) extends into the safe area.
- The header's content (chat title, buttons) is pushed down by the safe area size.
- Implementation: add a `<div class="safe-area-top">` as the first child inside `.layout-header`, before `.c-content`.
- **Listening panel consideration:** when `.header-activity-panel-wrapper` is visible, the header grows to `max-height: 7.5rem`. The listening panel (`.chat-activity-panel`) uses `absolute bottom-0.5` positioning. The safe area adds to the header's total height, but the listening panel's absolute positioning from `bottom` should be unaffected. The `max-height` values may need adjustment by `env(safe-area-inset-top)`.

**Bottom safe area — part of editor:**
- The safe area should be part of the message editor area (`.layout-footer` / `.writable-chat-footer`).
- Its color matches the editor background (`bg-post-panel` on narrow, `bg-01` on wide).
- It pushes the editor content (input field, buttons) upward.
- Implementation: add a `<div class="safe-area-bottom">` as the last child inside `.layout-footer` (after the editor), or at the bottom of `.writable-chat-footer`.
- The safe area's background matches the footer's background so it looks like an extension.

**Key files:**
- `UI.Blazor/Components/PageWithHeaderAndFooter.razor`
- `UI.Blazor.App/Components/ChatHeader.razor`
- `UI.Blazor.App/Components/ChatActivityPanel/ChatActivityPanel.razor`
- `UI.Blazor.App/Components/ChatActivityPanel/chat-activity-panel.css`
- `UI.Blazor.App/Components/ChatFooter.razor`
- `UI.Blazor.App/Components/ChatMessageEditor/ChatMessageEditor.razor`
- `UI.Blazor.App/Components/ChatMessageEditor/chat-message-editor.css`
- `styles/main.css`

---

## Components Requiring Safe Area Support

### 1. Full-Screen Overlays (all 4 edges, transparent)

| Component | CSS Selector | CSS File | Razor File | Safe Areas | Transparent |
|-----------|-------------|----------|------------|------------|-------------|
| Modal Overlay | `.modal-overlay` | `UI.Blazor/Components/Modal/modal.css` | `UI.Blazor/Components/Modal/Modal.razor` | all 4 | yes |
| Modal Chrome Overlay | `.modal-chrome-overlay` | `UI.Blazor/Components/Modal/modal.css` | `UI.Blazor/Components/Modal/ModalChrome.razor` | all 4 | yes |
| Menu Host | `.ac-menu-host` | `UI.Blazor/Components/Menu/menu.css` | (Menu host component) | all 4 | yes |
| Menu Overlay | `.ac-menu-overlay` | `UI.Blazor/Components/Menu/menu.css` | (Menu host component) | all 4 | yes |
| Bubble Host | `.ac-bubble-host` | `UI.Blazor/Components/Bubble/bubble.css` | `UI.Blazor/Components/Bubble/BubbleHost.razor` | all 4 | yes |
| Reconnect Overlay | `.reconnect-overlay` | `UI.Blazor/Components/Overlays/reconnect-overlay.css` | `UI.Blazor/Components/Overlays/ReconnectOverlay.razor` | all 4 | yes |
| Web Splash | `.web-splash` | `UI.Blazor/Components/Overlays/web-splash.css` | `UI.Blazor/Components/Overlays/WebSplash.razor` | all 4 | yes |
| Video Panel (expanded) | `.video-panel.expanded` | `UI.Blazor.App/Components/VideoPanel/video-panel.css` | `UI.Blazor.App/Components/VideoPanel/VideoPanel.razor` | all 4 | yes |

**Positioning details:**
- `.modal-overlay` — `fixed top-0 left-0 h-full w-full`. All modals render inside this div (via `Modal.razor` which wraps `ModalHost` entries). The overlay class is set via `ModalOptions.OverlayClass`.
- `.modal-chrome-overlay` — `fixed left-0 top-0 h-full w-full`. Alternate backdrop rendered inside `ModalChrome.razor`.
- `.ac-menu-host` — `fixed z-menu-container overflow-hidden`. Gets `inset-0` via `:has(.ac-menu)`. Full-screen menu overlay on mobile.
- `.ac-menu-overlay` — `fixed inset-0 overflow-hidden z-menu-overlay`. Semi-transparent backdrop behind menus.
- `.ac-bubble-host` — `fixed z-bubble overflow-hidden`. Gets `inset-0` via `:has(.ac-bubble)`. Floating UI element container.
- `.reconnect-overlay` — `absolute inset-0 z-[1900]`. Inside a fixed ancestor. Connection loss UI.
- `.web-splash` — `absolute z-[1900] w-screen h-screen`. Initial loading screen.
- `.video-panel.expanded` — `fixed z-tooltip inset-0`. Full-screen video call mode.

### 2. Side Navigation Panels (all 4 edges on narrow; top+bottom+one side on wide)

| Component | CSS Selector | CSS File | Razor File | Safe Areas (narrow) | Safe Areas (wide) | Transparent |
|-----------|-------------|----------|------------|--------------------|--------------------|-------------|
| SideNav Left | `.side-nav.side-nav-left` | `UI.Blazor/Components/SideNav/side-nav.css` | `UI.Blazor/Components/SideNav/SideNav.razor` | all 4 | top, bottom, left | no |
| SideNav Right | `.side-nav.side-nav-right` | `UI.Blazor/Components/SideNav/side-nav.css` | `UI.Blazor/Components/SideNav/SideNav.razor` | all 4 | top, bottom, right | no |

**Positioning details:**
- `.side-nav` — `fixed md:relative top-0 bottom-0 left-0 h-full w-full`. On narrow: fixed full-screen. On wide (`md+`): `relative`, positioned side by side.
- Left panel: `flex-none flex-y`. Width grows at breakpoints: `md:w-96 lg:w-112 xl:w-128 2xl:w-144`. Slides off-screen with `translate3d(-100%, 0, 0)` when closed.
- Right panel: `flex-none flex-y`. Width grows at breakpoints: `lg:w-96 xl:w-112 2xl:w-128`. On tablet (820px-1280px): `absolute w-1/2 left-1/2`. Slides off-screen with `translate3d(100%, 0, 0)` when closed.

### 3. Main Layout — Base Layout (left, right always; top/bottom in middle panel)

| Component | CSS Selector | CSS File | Razor File | Safe Areas | Transparent |
|-----------|-------------|----------|------------|------------|-------------|
| Base Layout | `.base-layout` | `styles/main.css` | `UI.Blazor/Layouts/BaseLayout.razor` | left, right (always) | no (black) |
| Layout Header | `.layout-header` | `styles/main.css` | `UI.Blazor/Components/PageWithHeaderAndFooter.razor` | top (inside header) | no |
| Layout Footer | `.layout-footer` | `styles/main.css` | `UI.Blazor/Components/PageWithHeaderAndFooter.razor` | bottom (inside footer) | no |

**Layout structure (BaseLayout.razor):**
```
<div class="base-layout">
  <div class="base-layout-body">
    <div class="c-layout-content">
      <ToastHost/>
      <Body/> ← renders PageWithHeaderAndFooter
    </div>
  </div>
</div>
```

**Layout structure (PageWithHeaderAndFooter.razor):**
```
<div class="page-with-header-and-footer">
  ├─ LeftDrawer (SideNav left)
  ├─ <div class="@Class">  ← "default-layout" or "docs-layout"
  │    ├─ .layout-header
  │    │    ├─ [safe-area-top] ← FIRST CHILD, pushes header content down
  │    │    └─ .c-content (ChatHeader)
  │    ├─ .layout-subheader
  │    ├─ .layout-body-wrapper
  │    │    └─ .c-container
  │    │         ├─ .layout-footer
  │    │         │    ├─ ChatFooter / ChatMessageEditor
  │    │         │    └─ [safe-area-bottom] ← LAST CHILD, pushes editor content up
  │    │         ├─ .layout-subfooter
  │    │         └─ .layout-body (scrollable messages)
  └─ RightDrawer (SideNav right)
</div>
```

### 4. Landing Page (all 4 edges, transparent)

| Component | CSS Selector | CSS File | Razor File | Safe Areas | Transparent |
|-----------|-------------|----------|------------|------------|-------------|
| Landing Page | `.landing` | `UI.Blazor.App/Pages/Landing/landing.css` | `UI.Blazor.App/Pages/Landing/LandingForWeb.razor` | all 4 | yes |
| Landing Header | `.landing-header` | `UI.Blazor.App/Pages/Landing/landing.css` | `UI.Blazor.App/Pages/Landing/LandingHeader.razor` | left, top, right | yes |
| Landing Page Links | `.page-links` | `UI.Blazor.App/Pages/Landing/landing.css` | (LandingDownloadLinks component) | bottom | yes |
| Landing Mobile Menu | `.landing-menu` | `UI.Blazor.App/Pages/Landing/landing.css` | `UI.Blazor.App/Pages/Landing/LandingLeftMenu.razor` | all 4 (narrow only) | yes |

### 5. Docs Pages (all 4 edges, transparent)

| Component | CSS Selector | CSS File | Razor File | Safe Areas | Transparent |
|-----------|-------------|----------|------------|------------|-------------|
| Docs Layout | `.docs-layout` | `UI.Blazor.App/Pages/Landing/landing.css` | `UI.Blazor.App/Pages/Landing/Docs/DocsLayout.cs` | all 4 | yes |

### 6. Full-Screen Modal Dialogs

Full-screen dialogs (settings, onboarding, etc.) should exclude left/right safe areas — those are always handled by `base-layout`.

| Component | CSS Selector | CSS File | Razor File | Safe Areas | Notes |
|-----------|-------------|----------|------------|------------|-------|
| Full-screen modal frame | `.modal-overlay-fullscreen .modal-frame.modal-chrome` | `UI.Blazor/Components/Modal/modal.css` | `UI.Blazor/Components/Modal/ModalFrame.razor` | top, bottom | Left/right excluded (handled by base-layout) |

**Top safe area — part of dialog header:**
- The safe area div goes inside the dialog's header (`.modal-header` or `.modal-header-interactive`).
- It makes the header taller, extending the header's background color to the top of the screen.
- The header content (title, close button) is pushed down by the safe area size.

**Bottom safe area — part of dialog container:**
- The safe area div goes at the bottom of the dialog frame (`.modal-frame`), after the dialog content.
- It extends the dialog's bottom, pushing content (buttons, etc.) away from the bottom edge.
- Color matches the dialog background.

### 7. Bottom-Sheet Modal Dialogs (narrow only)

| Component | CSS Selector | CSS File | Razor File | Safe Areas (narrow) | Safe Areas (wide) | Transparent |
|-----------|-------------|----------|------------|--------------------|--------------------|-------------|
| Modal Frame (md) | `.modal-frame.modal-md` | `UI.Blazor/Components/Modal/modal.css` | `UI.Blazor/Components/Modal/ModalFrame.razor` | bottom | none | no |
| Modal Frame (sm) | `.modal-frame.modal-sm` | `UI.Blazor/Components/Modal/modal.css` | `UI.Blazor/Components/Modal/ModalFrame.razor` | none | none | — |

- `body.narrow .modal-frame.modal-md` — `absolute bottom-0 left-0 right-0; height: 80vh`. Bottom sheet. Bottom safe area inside the frame pushes dialog buttons up.
- `body.narrow .modal-frame.modal-sm` — `absolute left-4 right-4; bottom: 4vh`. Floating centered, doesn't touch edges — no safe area needed.

### 8. Menus (all 4 edges on overlay, transparent)

Menu overlays (`.ac-menu-host`, `.ac-menu-overlay`) get all 4 safe areas with transparent + backdrop blur. The safe areas push the menu content away from screen borders.

The menu content (`.ac-menu`) itself doesn't need safe areas — it's positioned by JS within the safe area boundaries.

### 9. Image/Media Viewer (all 4 edges)

| Component | CSS Selector | CSS File | Razor File | Safe Areas | Transparent |
|-----------|-------------|----------|------------|------------|-------------|
| Image Viewer Modal | `.image-viewer-modal.modal-frame` | `UI.Blazor/Components/VisualMediaViewerModal/visual-media-viewer-modal.css` | `UI.Blazor/Components/VisualMediaViewerModal/VisualMediaViewerModal.razor` | all 4 | yes |

### 10. Toast Container (offset adjustment)

| Component | CSS Selector | CSS File | Razor File | Safe Areas (narrow) | Safe Areas (wide) |
|-----------|-------------|----------|------------|--------------------|--------------------|
| Toast Container | `.toast-container` | `UI.Blazor/Components/Toast/toast.css` | `UI.Blazor/Components/Toast/ToastHost.razor` | top offset | bottom offset |

- CSS `calc()` adjustment on `top`/`bottom` offsets to account for safe area insets.

### 11. Cookie/Minor Fixed Elements

| Component | CSS Selector | CSS File | Razor File | Safe Areas |
|-----------|-------------|----------|------------|------------|
| Cookie Settings | `.cookie-settings` | `UI.Blazor.App/Pages/Landing/landing.css` | (CookieSettings component) | bottom, left offset |

- CSS `calc()` adjustment on `bottom` and `left` offsets.

---

## Positioning Logic Check

Components that calculate element placement via JS may need adjustment for safe area presence:

| Component | File | Logic | Safe Area Impact |
|-----------|------|-------|------------------|
| Bubble positioning | `UI.Blazor/Components/Bubble/bubble.ts` | Calculates absolute position to point at a target element | May need to offset by safe area insets if bubble appears near screen edges |
| Menu positioning | `UI.Blazor/Components/Menu/menu.ts` | Calculates menu position relative to trigger element | May need to account for safe area insets in boundary calculations |
| SideNav gesture detection | `UI.Blazor/Components/SideNav/side-nav.ts` | Edge detection at 25px from screen edge using absolute coordinates | Already uses absolute screen coordinates — no change needed |

**Action:** Review bubble and menu positioning code to verify whether their boundary/clamping logic accounts for `env(safe-area-inset-*)` values. If they clamp to viewport edges, the clamp boundaries may need to be inset by the safe area amounts.

---

## Narrow vs Wide Behavior

**Narrow mode** (mobile, `body.narrow`):
- Side panels slide in full-screen — each panel handles top/bottom safe areas independently
- Left/right safe areas are always at `base-layout` (black)
- Middle panel: top safe area in header, bottom safe area in footer/editor
- Full-screen dialogs: top in header, bottom in container

**Wide mode** (tablet/desktop, `md+`):
- Side panels are positioned `relative`, side by side
- Left/right safe areas at `base-layout` (black) — same as narrow
- Top/bottom safe areas remain inside each panel's header/footer

**Left/right safe areas are ALWAYS at the top-level layout (`base-layout`), ALWAYS fully black, independent of narrow/wide mode.** Full-screen overlays that cover the entire screen should exclude left/right safe areas (those are behind the overlay, handled by `base-layout`).

---

## Implementation Order

1. CSS classes (`.safe-area-top/bottom/left/right`, `.safe-area-transparent`)
2. Base layout left/right safe areas
3. Landing page (transparent safe areas)
4. Docs pages (transparent safe areas)
5. Sign-in modals
6. Main layout — middle panel (header top, footer bottom)
7. Left panel (sidebar top/bottom, list bottom overlay)
8. Right panel (background image top, content bottom)
9. Full-screen dialogs (header top, container bottom)
10. Menus (transparent overlay safe areas)
11. Full-screen overlays (modals, bubbles, reconnect, splash, video)
12. Media viewer
13. Minor components (toasts, cookie banner)
14. Positioning logic review (bubbles, menus)
