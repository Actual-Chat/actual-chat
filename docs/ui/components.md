# UI Component Guidelines

This document describes conventions for creating Blazor UI components, their file structure, CSS styling, and TypeScript integration.

## File Structure

### Simple Component (no special styles)

A component that doesn't need custom styles can be a single `.razor` file placed alongside related components:

```
Components/
  SomeExisting.razor
  AmazingPanel.razor          ← no folder needed
```

### Component with Children or Custom Styles

When a component has children, custom styles, or TypeScript — create a dedicated folder:

```
Components/AmazingPanel/
  AmazingPanel.razor
  AmazingPanelHeader.razor    ← sub-component, no separate folder or CSS
  amazing-panel.css
  amazing-panel.ts            ← only if TypeScript is needed
```

- **Sub-components** (e.g., `AmazingPanelHeader.razor`) live in the same folder — they do NOT get their own folder or CSS file. They may have a separate `.ts` file if needed.
- **Small helper components** that exist only to be used inside a parent (e.g., `AmazingPanel` used inside `AmazingView`) can be placed in the parent's folder instead of getting their own.

### Components Inside a Container (Modal, Panel, View)

When a component lives inside a container that already has its own CSS file (e.g., tabs inside `SettingsModal`), do NOT create a separate CSS file. Add the component's styles to the container's CSS file instead, grouped under a section comment:

```css
/* ── API Keys tab ── */

.api-key-settings .c-key-info {
    @apply flex-y;
}
```

Structure the container's CSS file by sections/tabs so it's easy to find styles for each child component.

### Registration

- **CSS files** must be imported in `src/dotnet/UI.Blazor.App/styles.css` via `@import`.
- **TypeScript files** must be exported from the appropriate `exports.ts`.

## CSS Class Naming

### Root Element

The component's root element gets a kebab-case class matching the component name:

```razor
<!-- AmazingPanel.razor -->
<div class="amazing-panel">
    ...
</div>
```

### Child Elements

Direct children use the `c-` prefix:

```html
<div class="amazing-panel">
    <div class="c-content">...</div>
    <div class="c-left">...</div>
    <div class="c-right">...</div>
</div>
```

For **deep component hierarchies**, major blocks get their own named class instead of `c-` to avoid ambiguity:

```html
<div class="amazing-panel">
    <div class="amazing-panel-header">
        <div class="c-content">...</div>
        <div class="c-title">...</div>
    </div>
    <div class="amazing-panel-body">
        <div class="c-content">...</div>
    </div>
</div>
```

### Standard Sub-components

Standard components inside (buttons, separators, tiles, inputs) do NOT need extra classes if they use default styles. Add a scoped class only when overriding defaults:

```css
/* No extra class needed for default buttons */
.amazing-panel .btn { }

/* Add scoped class only for non-default styling */
.amazing-panel .btn.amazing-panel-btn { }
```

Button classes must **start** with `btn-`, followed by the purpose: `btn-primary`, `btn-save`, `btn-confirm`, `btn-rec`, `btn-pin-toggle`. Never put `btn` at the end (`rec-btn`, `save-form-btn`, `pin-toggle-btn`).

### Toggle State

For buttons and elements that behave like a toggle (on/off), use the `.on` class (and `.off` if needed). Do not invent custom state classes like `.video-active` or `.is-selected`:

```razor
<button class="@(isRecording ? "on" : "")">
```
```css
.amazing-panel > button.on {
    @apply outline-2 outline-primary;
}
```

### Semantic Host Classes for JS Queries

When TypeScript needs to find a parent container via `closest()`, add a dedicated semantic class to the host element instead of relying on layout-specific classes. This decouples the JS logic from the page structure:

**Wrong:**
```typescript
this.host = this.el.closest('.video-panel-chat') ?? this.el.closest('.list-view-layout');
```

**Correct:**
```typescript
this.host = this.el.closest('.upload-drag-drop-host');
```

Then apply the class to each host element in Razor/C#.

### Hover and Touch Styles

Use `body.hoverable` for hover styles (desktop with mouse) and `.touch-capable` for active/tap styles (touch devices). Do not use plain `:hover` or `:active` — hover can "stick" on touch devices after a tap:

```css
body.hoverable .amazing-panel > button:hover,
.touch-capable .amazing-panel > button:active {
    @apply text-primary;
}
```

## Blazor owns `class`

A Lit custom element rendered from Razor has **two** potential writers for its host
attributes — Blazor, which rewrites the element's markup on every re-render, and the
component itself. Anything both of them write ends up in a permanent tug-of-war,
because Blazor's render tree never learns what the component changed.

This is **not** limited to Lit. It applies to every plain-TS component that receives
a Razor element through `@ref`, and to every element it reaches from there
(`parentElement`, `closest()`, a `querySelector` hit that Razor also renders).

**The rule: a Lit component may *have* a `class`, but must never *write* one.**
`class` belongs to Blazor. The component owns only attributes Blazor does not render.

Publish component state through a `data-*` attribute and match it from CSS —
attribute selectors have the same specificity as class selectors, so nothing in the
cascade changes:

**Wrong** — the component writes `class`, Blazor overwrites it on the next render,
the component writes it again, forever:
```typescript
@property({ reflect: true }) class: string;   // makes every foreign class write re-render

updated(changed: Map<string, unknown>) {
    if (changed.has('class'))
        this.classList.add(`show-image-${this._state}`);   // fights Blazor
}
```
```css
image-skeleton.show-image-original .image { ... }
```

**Correct** — Blazor owns `class`, the component owns `data-image-state`:
```typescript
private applyState(): void {
    this.setAttribute('data-image-state', this._state);
}
```
```css
image-skeleton[data-image-state="original"] .image { ... }
```

Razor keeps writing `class` exactly as it would for any other element:
```razor
<image-skeleton class="pic-image @ExtraClass" src="@url" />
```

If the component needs an **initial** state from Razor, take it as a Lit property
(`@property({ attribute: 'initial-state' })`) rather than reading it back out of the
attribute the component itself writes. Razor renders that property as a constant, so
re-renders write the same value and never conflict.

The same reasoning applies to any attribute Razor emits — `style`, `title`, `id`. If
Razor renders it, the component must not write it.

### Why it looks intermittent

Blazor diffs per attribute and writes `class` only when the Razor-computed string
actually *changes*. A JS-added class therefore survives indefinitely on an element
whose Razor class is constant, and disappears the first time some unrelated
expression in that same attribute flips. That makes the failure look like a race
rather than a rule violation, and it hides in review: the offending line reads fine,
and the element it targets is several files away.

The iOS camera-preview bug was exactly this. `VideoStreamingPreview.razor` renders
`class="video-track-player video-streaming-preview @Class @recordingCls @screenCastCls"`,
and `RecorderPreviewView` set `preview-backend-mstg` on that same element to hide the
idle render surface. Starting a recording flipped `@recordingCls` from `""` to
`"recording"`, Blazor rewrote `class`, and the backend class vanished — leaving a
never-painted `<canvas>` (`z-index: 1`) covering the live `<video>` beneath it. The
preview went blank while the camera and the outbound stream were working perfectly.

To find the rest of them, cross-reference two lists: Razor elements carrying both
`@ref` and a `@`-interpolated `class`, and TS lines writing `classList`/`className`
on `this.element` / `this.ref` / `parentElement` / `closest()`.

## A promoted `<video>` layer can decode without painting

On iOS WebKit a `<video>` can hold a healthy, advancing stream and still paint
nothing. The self-preview hit this after a camera switch: the element was
`display: block`, `visibility: visible`, `paused: false`, `readyState: 4`, at the
right size and on the right track, and `drawImage(videoEl)` returned **fresh
pixels every frame** — while the screen showed an empty rectangle.

The trigger is compositing, not media. `.video-track-player` sets
`contain: strict` and the surfaces inside it carry `will-change: transform`
(plus `scaleX(-1)` when the camera is mirrored). Re-attaching a track to a
promoted layer inside a strictly-contained parent can leave WebKit with a
GraphicsLayer it never repaints. Both properties are load-bearing — see the
`contain` / `will-change` notes in `video-panel.css` — so the layer is nudged
instead: `forceRecomposite()` in `recorder-preview-view.ts` toggles `display`
once on `loadeddata`, which forces a fresh compositing pass.

**How to tell this apart from a frame-delivery failure**, because they look
identical on screen:

- `<video>.currentTime` is **useless** — it advances with the wall clock on a
  MediaStream-backed element whether or not frames arrive. A fully frozen
  preview reported 14.992 s of progress over 14.993 s.
- `requestVideoFrameCallback` is also useless on its own: `presentedFrames`
  keeps incrementing on a frozen element and `mediaTime` sits at 0.
- **Hash the pixels.** Draw the element into a small offscreen canvas, hash the
  `ImageData`, and compare samples seconds apart. Changing hash + blank screen
  ⇒ compositing. Constant hash ⇒ delivery.

If it is delivery, the sender-side tally is already there: turn on
`logLevels.override('*Video*', 1)` and read `previewTrace` (forwarded / refused /
written / resolved / inFlight) and `recorderStats` (captured / offered / encoded
/ shipped) on the main thread. The preview tap runs in a worker whose console the
inspector cannot reach, so `previewTrace` is pulled over RPC — never add
diagnostic `console` output in that realm expecting to read it.

## No Inline Tailwind in Razor

Do NOT write Tailwind utility classes directly in `.razor` markup. Instead, assign a CSS class and use `@apply` in the CSS file.

**Wrong:**
```razor
<div class="flex flex-col gap-4 p-4 text-sm text-03">
```

**Correct:**
```razor
<div class="c-content">
```
```css
.amazing-panel .c-content {
    @apply flex-y gap-4;
    @apply p-4;
    @apply text-sm text-03;
}
```

**Exception:** Tailwind classes may appear in `.razor` when passed as a parameter or conditionally applied:

```razor
<Button Class="@(_isActive ? "btn-primary" : "btn-secondary")" />
```

## Scrollbars

### Hiding scrollbars: `no-scrollbar`

`tailwind.css` defines a `.no-scrollbar` utility that hides the scrollbar in both Firefox and webkit browsers:

```css
.no-scrollbar               { scrollbar-width: none; }
.no-scrollbar::-webkit-scrollbar { width: 0; height: 0; }
```

**Always put `no-scrollbar` directly on the element in Razor/HTML — not via `@apply` in component CSS.**

```razor
<div class="member-list no-scrollbar">
```

`@apply no-scrollbar` only inlines `scrollbar-width: none` (Firefox). The `::-webkit-scrollbar` pseudo rule is a separate selector and is not pulled in by `@apply`, so the scrollbar stays visible on webkit. Adding the class on the element makes both rules match the same node and gives one source of truth.

When the element is rendered inside a shared component, prefer passing the class through the component's class parameter:

| Component | Parameter | Targets |
|---|---|---|
| `DialogFrame` / `DiveInDialogFrame` | `BodyClass` | `.dialog-body` |
| `PageWithHeaderAndFooter` (via `DefaultLayout.BodyClass`) | `BodyClass` | `.layout-body` |
| `Stepper` | `ContentClass` | `.stepper-content` |
| `SettingsTab` | `HeaderClass` / `ContentClass` | `.settings-tab-header` / `.settings-tab-content` |
| `MarkupEditor` | `ContentClass` | `.editor-content` |

When neither direct HTML nor a class parameter is feasible (e.g. a `:has()`-conditional rule), keep the local CSS but write the **paired FF + webkit** version so both browsers behave the same:

```css
.layout-body:has(.chat-panel-skeleton)              { scrollbar-width: none; }
.layout-body:has(.chat-panel-skeleton)::-webkit-scrollbar { display: none; }
```

### Custom scrollbar: `custom-scrollbar` / `custom-scrollbar-x`

`tailwind.css` also defines thin styled scrollbars (`.custom-scrollbar`, `.custom-scrollbar-x`, `.custom-scrollbar-outside`). These are normal Tailwind utilities — use them via `@apply` or directly in Razor as you would any other class.

## CSS File Structure

### Selector Ordering

Selectors go from parent to children, top to bottom:

```css
.amazing-panel { }
.amazing-panel .c-header { }
.amazing-panel .c-content { }
.amazing-panel .c-content .btn.amazing-panel-btn { }
```

### Property Ordering Within a Rule

Properties are grouped by category, top to bottom. Use `@apply` for Tailwind utilities, then raw CSS below:

```css
.amazing-panel {
    /* 1. Positioning */
    @apply absolute top-0 z-10;

    /* 2. Display, flex, grid, gap, alignment */
    @apply flex-y items-center justify-center gap-4;

    /* 3. Width, height (including min/max) */
    @apply w-full min-h-48;

    /* 4. Padding, margin */
    @apply p-4 mx-2;

    /* 5. Border (radius, width, color) */
    @apply rounded-lg border border-separator;

    /* 6. Overflow */
    @apply overflow-hidden;

    /* 7. Background */
    @apply bg-01;

    /* 8. Text (color, font-size, etc.) */
    @apply text-sm text-primary;

    /* 9. Pointer-events, cursor, etc. */
    @apply pointer-events-auto cursor-pointer;

    /* 10. Transform, transition */
    @apply transition-opacity;
    transform: scale(1.2);

    /* 11. Raw CSS not available in Tailwind */
    backdrop-filter: blur(8px);

    /* 12. Animations (always last) */
    animation: fadeIn 0.3s ease-in-out;
}
```

Not all categories are present in every rule — include only what's needed. Separate comments are optional.

Each category gets its own `@apply` line. Utilities from the same category share that line (`@apply w-full min-h-48`), but utilities from **different** categories must **not** be combined, even when each is short — e.g. write

```css
@apply relative;
@apply flex items-center justify-center;
```

not `@apply relative flex items-center justify-center;`. The category order above still applies whether or not comments are present.

## Animation Performance

Animations can cause expensive full-document repaints if the browser can't isolate them to a compositor layer. Follow these rules to keep animations cheap:

### Layer Isolation

Add `will-change: transform` and `contain: layout paint style` to any element that runs a CSS animation or transition. This tells the browser to promote the element to its own compositor layer and prevents layout/paint from leaking to ancestors:

```css
.c-film-strip {
    will-change: transform;
    contain: layout paint style;
}
```

Apply it to the **animated element itself**, not a distant parent. If a component has several independent animations (e.g., the root element and a pseudo-element wrapper), each one gets its own pair.

**Caveat:** Don't add `will-change: transform` blindly — each promoted layer consumes GPU memory. On Android WebView, too many compositor layers can cause rendering corruption (e.g., black circles instead of avatars). If you see visual glitches on mobile, reducing the layer count is the first thing to try. See `navbar.css` for a real example of this trade-off.

### Pause Invisible Animations

When an animated element is hidden (e.g., via `opacity: 0` or a state class), pause the animation so it doesn't burn CPU/GPU cycles offscreen:

```css
.chat-activity-panel.watching .c-film-strip {
    @apply opacity-0;
}
.chat-activity-panel.watching .c-film-strip::after {
    animation-play-state: paused;
}
```

Always pair `opacity: 0` (or `visibility: hidden`) with `animation-play-state: paused` on the animated child.

### Prefer Composited Properties (mandatory)

Always choose the cheapest animation technique that achieves the desired visual effect. The hierarchy from cheapest to most expensive:

1. **`opacity`** — composited, zero repaint
2. **`transform`** (`translate`, `scale`, `rotate`) — composited, zero repaint
3. **`clip-path`** — composited in modern browsers, good for reveal/hide effects that need to preserve `border-radius`
4. **`filter`** (`blur`, `brightness`, `drop-shadow`) — GPU-accelerated but heavier than transform/opacity
5. **`background-color`**, **`border-color`** — repaint (no layout, but still non-composited)
6. **`box-shadow`**, **`outline`**, **`border-radius`** — repaint, can be expensive with blur
7. **`width`**, **`height`**, **`top`**, **`left`**, **`margin`**, **`padding`** — layout + repaint, the most expensive

**Rule:** never use levels 5-7 for infinite or long-running animations. For one-shot animations (e.g., modal open, 0.3s forwards), levels 5-6 are acceptable. Level 7 should be avoided even for one-shot animations — use `transform: scale()` / `translate()` or `clip-path: inset()` instead.

Common replacements:
- `width`/`height` animation → `clip-path: inset(… round …)` (preserves border-radius) or `transform: scale()` (distorts content)
- `left`/`top` animation → `transform: translate()`
- `box-shadow` glow → `::after` with fixed `box-shadow` + `opacity` animation
- `background-color` pulse → fixed `background-color` + `opacity` animation
- `background-position` scroll → `transform: translateX()` on the element or pseudo-element
- `background-clip: text` shimmer → `::before` overlay with translucent gradient + `transform: translateX()`
- SVG `stroke-dashoffset` perimeter sweep → keep, but use `steps(N)` (see below); a composited replacement requires a child overlay with a mask, rarely worth the complexity

### Don't mix paint-thread properties into composited keyframes

A `transform`-only or `opacity`-only keyframe stays on the compositor. The moment you add `filter`, `box-shadow`, `background-color`, or any other paint property into the same keyframe, the entire animation falls back to paint thread:

```css
/* BAD — blur kills the compositor path for the whole animation */
@keyframes timer-swap {
    0%   { opacity: 0; filter: blur(4px); }
    100% { opacity: 1; filter: blur(0); }
}

/* GOOD — opacity-only stays composited */
@keyframes timer-swap {
    0%   { opacity: 0; }
    100% { opacity: 1; }
}
```

Same principle when stacking effects: a `box-shadow` glow combined with `transform: scale()` in one keyframe gives the worst of both worlds. Split: keep `transform` on the parent's keyframe, put the `box-shadow` on a `::after` and animate its `opacity`.

### Reduce paint frequency for unavoidable paint-thread animations

Some properties — SVG `stroke-dashoffset`, `stroke-dasharray`, `background-position`, `mask-position` — can't be moved to the compositor. When the visual effect requires one of them, use the `steps(N)` timing function to lower the effective paint rate:

```css
.c-film-strip::after {
    /* 30 s × 60 fps = 1800 paints/cycle → with steps(300) only 300 paints */
    animation: film-scroll 30s steps(300) infinite;
}
.video-icon .frame {
    /* film-strip dashed-border sweep at ~10 fps */
    animation: frame-gap 4s steps(40) infinite;
}
```

`steps(N)` makes the browser only update the rendered value at step boundaries, so paint frequency drops from 60 fps to `N / duration`. The motion looks like an old film projector (small discrete jumps), which usually reads as intentional and avoids a continuous 60 fps paint loop. Always pair with `contain: layout paint style` on the parent so each repaint stays inside a tiny layer.

### Phase-align looping stepped animations (AnimationSync)

The cost of a rendering update is dominated by a fixed per-frame overhead, not by how many elements changed. Left alone, each looping animation starts whenever its element appears, so their ticks scatter across frames — with enough of them nearly every frame contains a tick and that fixed cost is paid ~60×/s. Measured on an iPhone 13 Pro: 8 unsynchronised looping animations cost +0.45 cores over idle and 40 cost +0.59 — but the same 40 **phase-aligned** cost +0.08.

`AnimationSync` (`src/nodejs/src/animation-sync.ts`) fixes this by back-dating `animation-delay` so every synced animation ticks on a shared 100 ms grid (`animationGridMs`). Alignment happens the moment an element appears — nothing is deferred.

**Only looping, stepped animations benefit — and this is exactly the interaction with `steps(N)` above:**

- **Stepped + looping** → register it. `steps(N)` lowers the paint rate; AnimationSync makes those paints *coincide* in the same frames. Both together is the pattern for any looping stepped animation that runs alongside others (call UI, skeletons, spinners).
- **Continuous (not stepped)** → don't register. It changes every frame regardless of phase, so aligning can't help; AnimationSync logs a `console.warn` rather than pretending to. If it's composited (`transform`/`opacity`), it's already cheap per frame — leave it.
- **One-shot** (a fade-in, `... forwards`) → don't register. Nothing recurring to amortise.

The tick — `animation-duration / steps` — must be a multiple of 100 ms, or two animations can never share an instant (AnimationSync warns if it isn't). A 2 s `steps(20)` = 100 ms tick and a 1.5 s `steps(15)` = 100 ms tick share the grid; `steps(300)` over 30 s = 100 ms tick likewise.

Three ways to register, in order of preference:

1. **By class** — add the element's class to the `animationClasses` set in `animation-sync.ts`. That set is the one place that lists every synced animation; keep it that way.
2. **By pseudo-element** — if the animation lives on `::before`/`::after` (not the element itself), also add a `class → pseudo` entry to `pseudoByClass`. A pseudo's animation is invisible in the host's computed style, so it must be declared. The phase is published as a `--anim-phase` custom property the pseudo inherits; its rule opts in with `animation-delay: var(--anim-phase, 0s)`.
3. **By attribute** — for nodes with no registered class (notably shadow-root nodes and bare `<path>`/`<rect>` in Lit SVGs), add `data-anim-sync`. Forms: bare (grid tick), `"::before"` (name a pseudo), `"200"` (coarser tick), `"200 ::before"` (both).

Triggering is automatic: **Blazor and plain-JS code call nothing** — `MutationProcessor` sweeps the document as elements arrive, and an `animationstart` listener catches elements that gain an animation later via a class change. **Lit components must call `AnimationSync.syncAll(this.renderRoot)` themselves** in render — `querySelectorAll` cannot cross a shadow boundary.

When a *write* (not a CSS animation) is stream-driven — e.g. a value updated from an RPC stream — schedule it with `fastRaf10` (`src/nodejs/src/fast-raf.ts`), the same 100 ms grid, so it lands in a frame the synced animations were changing anyway instead of adding one of its own.

### Avoid SVG SMIL animations

Do not use `<animateTransform>`, `<animate>`, or any other SMIL inside SVGs. SMIL animations:

- run on the paint thread (not composited),
- ignore CSS `animation-play-state: paused`,
- can only be stopped via JS `svg.pauseAnimations()` per individual SVG element,
- have inconsistent browser support and are deprecated in some engines.

Replace SMIL with CSS keyframes on a CSS-styleable SVG property (`transform`, `opacity`, `stroke-dashoffset` with `steps()`, etc.) — or just drop the animation if it's a decorative detail on a tiny element (e.g., a 3 s gradient rotation on a 24 × 24 px icon is invisible to the user but expensive to paint).

### Respect `prefers-reduced-motion`

Decorative animations (film strips, placeholder waves) should be hidden when the user prefers reduced motion:

```css
@media (prefers-reduced-motion: reduce) {
    .c-film-strip {
        @apply hidden;
    }
}
```

## Custom Sliders

Styling `<input type="range">` directly has hard browser limits — the native thumb's position is computed internally and can't be re-aligned with a custom-painted track fill. The two visible symptoms in practice: thumb spills past the bar's edges at `value=0` / `value=max`, and during drag the gradient fill lags one frame behind the thumb (gradient repaints on paint thread, thumb moves on compositor).

**Pattern:** keep the native input as a transparent hit-target overlay, render visual elements (track, fill, thumb) as sibling `<div>`s driven by a single `--progress` CSS variable.

```html
<div class="slider" style="--progress: 0.42">
    <div class="c-track"></div>
    <div class="c-fill"></div>
    <div class="c-thumb"></div>
    <input type="range" min="0" max="1" step="0.001" value="0.42" />
</div>
```

```css
.slider {
    @apply relative;
    container-type: inline-size;  /* enables 100cqw inside */
}
.slider .c-track,
.slider .c-fill {
    @apply absolute left-0 right-0 top-1/2 h-0.5 rounded-full;
}
.slider .c-track {
    @apply -translate-y-1/2;
}
.slider .c-fill {
    @apply origin-left;
    transform: translateY(-50%) scaleX(var(--progress, 0));  /* composite-only */
}
.slider .c-thumb {
    @apply absolute top-1/2 w-2 h-2 -mt-1 rounded-full;
    transform: translateX(calc(var(--progress, 0) * (100cqw - 100%)));
}
.slider input {
    @apply absolute inset-0 w-full h-full m-0 cursor-pointer opacity-0;
    -webkit-appearance: none;
    appearance: none;
}
```

**Key tricks:**

- **`container-type: inline-size` + `100cqw - 100%`** positions the thumb so its left edge ranges from `0` to `(container - thumb_width)`. `100cqw` is the container's inline size; `100%` inside a transform refers to the element's own width. The math doesn't need to know the thumb's size — change `width` on the thumb and it stays aligned.
- **`scaleX` on the fill is composite-only.** No paint per frame. Both fill and thumb read the same `--progress` and animate on the compositor — they stay perfectly synced during drag.
- **Transparent native input on top** captures pointer events and emits `input` / `change` events that drive `--progress`. Keyboard nav (Arrow / Home / End) and screen-reader semantics work for free.

**Optimistic update during drag.** A document-level `input` listener that sets `--progress` directly on the slider element bypasses the C#/Blazor round-trip. Without it, the fill lags ~50 ms behind the thumb during fast drags:

```ts
document.addEventListener('input', (e) => {
    const t = e.target;
    if (t instanceof HTMLInputElement && t.classList.contains('slider-input'))
        t.parentElement?.style.setProperty('--progress', t.value);
}, true);
```

Real usage: `Components/AudioAttachmentPlayer/` (audio scrubber), `VisualMediaViewerModal/visual-media-viewer.ts` (video scrubber — same `100cqw - 100%` trick, native `<progress>` element kept for the fill).

## Atomic Content Swap Inside an Animated Container

When two components are mutually exclusive ("one or the other, never both") and each owns its own animated container (subheader, banner, toast), swapping between them runs both animations in parallel — outgoing exit + incoming enter — and the user sees a "dance" as one container shrinks while the other grows.

**Pattern:** one parent owns the animated wrapper, multiple inner components render their content inside. The wrapper only animates on the outermost transition (any active ↔ none active), never on swaps between modes.

```razor
@* AudioSubHeader.razor — one container, two possible bodies *@
@inherits ComputedStateComponent<AppUIHub, bool>
@{ var isVisible = State.Value; }

<SubHeader IsVisible="@isVisible">
    <ReplayBody />        @* self-hides via @if when its state is null *@
    <AttachmentBody />    @* same *@
</SubHeader>

@code {
    protected override async Task<bool> ComputeState(CancellationToken ct) {
        var a = await StateA.Use(ct).ConfigureAwait(false);
        if (a is not null) return true;
        var b = await StateB.Use(ct).ConfigureAwait(false);
        return b is not null;
    }
}
```

Each inner body keeps its own `ComputedStateComponent` subscribed to its own source — no coupling between them, no need to extract their state to the parent.

**Two non-obvious gotchas:**

### 1. State-update ordering — set the new state *before* clearing the old

When the action that triggers a swap modifies both source states (e.g. `Play()` stops the replay and starts attachment playback), order matters:

```csharp
// WRONG — opens a one-frame window where both are null
ChatAudioUI.StopReplay();
_attachmentState.Value = new PlaybackState { ... };

// CORRECT — overlap window where both are non-null instead
_attachmentState.Value = new PlaybackState { ... };
ChatAudioUI.StopReplay();
```

With the wrong order, the parent's `ComputeState` sees `both null` for one synchronous step → `IsVisible` flips to `false` → exit animation queued → next step flips it back → enter animation queued. Browser sees `enter → exit → enter` and may visibly start the exit before the enter overrides it. Reversed, the parent always sees "at least one non-null" → `IsVisible` stays `true` throughout.

The same applies to fire-and-forget cross-stops: `_ = OtherService.Stop()` at the top of an async method races with the rest of the method body and can resolve to "both null" while the method is awaiting something. Move the fire-and-forget *after* the new state is committed.

### 2. Children may not re-render in lock-step — guard the container's height

Each inner body has its own `ComputedStateComponent` with its own recompute schedule. Fusion's `UpdateDelayer.NextTick` batches *within* a single state, but two independent children watching two different sources can re-render in different Blazor frames. Result: for one paint frame, body A has gone empty (its `@if` flipped to `false`) and body B hasn't rendered yet (its `@if` still `false` from initial state). The container's slot is briefly empty → content-driven height collapses to 0 → user sees the container close and reopen even though `IsVisible` never flipped.

Cheapest fix: pin a `min-height` on the container's host so its rendered height never falls below the steady-state value:

```css
.audio-subheader {
    --subheader-height: 3.5rem;
    min-height: 3.5rem;
}
```

This doesn't break the enter/exit animation — `max-height` in the keyframes wins over `min-height` when they conflict (per CSS spec), so the container still shrinks to 0 during the exit keyframe.

Real usage: `Components/AudioSubHeader/`.

## Icons

UI icons are font glyphs generated from per-icon SVG sources. Use them in
markup via `<i class="icon-<name>"></i>`; the glyph takes `currentColor`,
so color and size are controlled with the usual text utilities
(`text-primary`, `text-2xl`, ...).

### Where icons live

| What | Where |
|---|---|
| SVG sources, one file per icon | `src/nodejs/icons/<name>.svg` |
| Generated font + CSS (never edit by hand) | `src/nodejs/fonts/svgtofont/` |

Both the sources **and** the generated output are committed to git. The CSS
class is derived from the file name: `marker-pin.svg` → `.icon-marker-pin`.

### Adding a new icon

1. Export the icon from Figma as SVG (24x24 viewBox is the norm). Before
   drawing a new one, check `src/nodejs/icons/` for an existing glyph that
   already matches.
2. **Fills only — no strokes.** The font generator fills path interiors and
   drops stroke geometry, so a stroke-based icon comes out as a solid
   silhouette. Convert strokes to outlined paths first, e.g.
   `npx oslllo-svg-fixer -s <src-dir> -d <out-dir>`. The fill color in the
   source is irrelevant — glyphs are monochrome.
3. Name the file in kebab-case after what the icon **depicts**, not the
   feature using it (`map.svg`, `navigation-pointer.svg`); drop design-system
   suffixes like `-01`.
4. Save it to `src/nodejs/icons/` and run `./icons-to-font.cmd`
   (= `npm run font`) to regenerate `src/nodejs/fonts/svgtofont/`.
5. Commit the new source together with **all** regenerated files. Codepoints
   of existing icons shift when a new name sorts in between (assignment is
   alphabetical) — that's expected and safe, since the CSS and fonts are
   regenerated in the same run and always stay in sync.

### When the font is the wrong home

The glyph is a single monochrome outline filled with the **nonzero** rule, so
two kinds of icon must be a `.lit.ts` SVG component instead (see
`Components/MapView/marker-pin-live-svg.lit.ts` and `marker-pin-off-svg.lit.ts`):

- **Multi-color art** — one glyph, one `currentColor`.
- **Art built from overlapping contours** with opposing winding, e.g. a
  strike-through bar laid over a shape. Separate SVG paths render fine, but
  the font flattens them into one contour set and the overlaps punch holes.
  Booleaning the source into a single unioned path also works if the icon must
  stay in the font.

Size such a component with `width: 1em; height: 1em` on `:host` and fill with
`currentColor`, and it stays a drop-in replacement for `<i class="icon-*">`.

Note: `scripts/prepare-system-icons.cmd` is a different pipeline — it rasterizes
`src/dotnet/Media.Service/Resources/*.svg` (system chat avatars) to PNGs and
has nothing to do with the icon font.

## TypeScript Interop

### Class Structure

TypeScript classes that integrate with Blazor follow this pattern:

```typescript
import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { preventDefaultForEvent } from 'event-handling';

export class AmazingPanel implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();

    // Static factory for Blazor JS interop
    public static create(
        element: HTMLElement,
        blazorRef: DotNet.DotNetObject,
    ): AmazingPanel {
        return new AmazingPanel(element, blazorRef);
    }

    constructor(
        private readonly element: HTMLElement,
        blazorRef: DotNet.DotNetObject,
    ) {
        // Subscribe to DOM events with auto-cleanup
        fromEvent<MouseEvent>(element, 'click').pipe(
            takeUntil(this.disposed$),
        ).subscribe(e => this.handleClick(e));
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
```

Key conventions:
- **`static create()`** — factory method called from Blazor via `JS.InvokeAsync<IJSObjectReference>`
- **`Disposable` interface** — implement `dispose()` for cleanup, called from Blazor via `DisposeSilentlyAsync("dispose")`
- **`Subject` + `takeUntil`** — use RxJS `fromEvent` with `takeUntil(this.disposed$)` for DOM event subscriptions instead of manual `addEventListener`/`removeEventListener`. All subscriptions auto-unsubscribe on dispose.
- **`preventDefaultForEvent`** — use the shared helper from `event-handling` module instead of `e.preventDefault()`
- **`disposed$.closed`** — check this instead of a separate `disposed` boolean flag

### Blazor Side

```csharp
@code {
    // Use the module's ImportName for the JS method path
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AmazingPanel.create";

    private IJSObjectReference? _jsRef;
    private DotNetObjectReference<AmazingPanel>? _blazorRef;

    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!firstRender)
            return;

        _blazorRef = DotNetObjectReference.Create(this);
        _jsRef = await JS.InvokeAsync<IJSObjectReference>(
            JSCreateMethod, _elementRef, _blazorRef).ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync() {
        if (_jsRef is { } jsRef) {
            _jsRef = null;
            await jsRef.DisposeSilentlyAsync("dispose");
        }
        _blazorRef?.Dispose();
        _blazorRef = null;
    }
}
```

- **`ImportName`** — `BlazorUICoreModule.ImportName` (`"ui"`) for `UI.Blazor` components, `BlazorUIAppModule.ImportName` (`"blazorApp"`) for `UI.Blazor.App` components
- **`.ConfigureAwait(true)`** — use in UI code when accessing instance members after await

### Conditional host element — dispose and re-create with the element

The `if (!firstRender) return;` pattern above is correct only when the JS-bound element lives for the whole lifetime of the component. It breaks when a **persistent** component conditionally renders its host element — e.g. a component that returns a skeleton while its state is loading, then renders the real `@ref` element once data arrives:

```razor
@{
    if (m.Chat is null) {
        <RightPanelSkeleton />
        return;
    }
}
<div @ref="Ref" class="right-panel">...</div>
```

If the component instance itself outlives this toggle (here it's rendered unconditionally by a parent `SideNav`), the host element comes and goes while `OnAfterRenderAsync` keeps firing. Two failures follow from a naive `if (JSRef is null) create` guard:

1. When the element is removed (state goes back to skeleton), the old JS instance is **never disposed** — any document-level subscriptions it holds keep firing against a detached element.
2. When the element reappears, `JSRef` is still non-null, so `create` never runs again — the new element gets **no JS instance** and the feature silently dies.

**Correct** — dispose when the host disappears, create when it reappears:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender) {
    if (State.Value.Chat is null) {
        if (JSRef is null)
            return;
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null!;
        return;
    }
    JSRef ??= await JS.InvokeAsync<IJSObjectReference>(JSCreateMethod, Ref);
}
```

Gate on the **same condition** that decides whether the `@ref` element renders, not on `firstRender`. This matters most for JS instances with document-level or global listeners (see `right-panel-collapse.ts`, which subscribes to `DocumentEvents` and only stops on `dispose()`), since those leak past the element they were created for.

Real usage: `Components/RightPanel/RightPanelContent.razor`.

### State classes shared between Razor and JS

Blazor rewrites the whole `class` attribute whenever any expression in the binding changes. Any class JS added on the side gets dropped silently on the next unrelated re-render.

**Wrong** — JS is the sole writer of `expanded`:

```razor
<div @ref="Ref" class="panel @firstOpenCls @recordingCls">
```
```typescript
public expand(): void {
    document.body.appendChild(this.panel);
    this.panel.classList.add('expanded'); // wiped by the next Razor render
}
```

**Correct** — route the state class through Razor too, so re-renders preserve it:

```razor
@{
    var expandedCls = m.IsExpanded ? "expanded" : "";
}
<div @ref="Ref" class="panel @firstOpenCls @recordingCls @expandedCls">
```

This is critical when the class also gates DOM position. If JS reparented the element to `document.body` based on the class, a class wipe alone leaves the element stuck there with nothing signalling that it should come back.

### Reparenting Blazor-rendered elements

If JS moves a Blazor-rendered element out of its home (e.g. `document.body.appendChild` for fixed-positioning escape), capture the original parent in the constructor and restore it on teardown:

```typescript
constructor(panel: HTMLElement, blazorRef: DotNet.DotNetObject) {
    this.panel = panel;
    this.home = panel.parentElement; // restore target
}
```

For defense-in-depth, add a MutationObserver on the element's class attribute that returns it to `this.home` when none of the state classes that should hold it elsewhere are present. Cheap, catches future races regardless of cause.

## Document-Level Mechanisms

Three mechanisms live outside the component and are opted into purely from markup. Two of them —
presence classes and animation sync — and one of the two render-script dispatchers are driven from
`mutation-processor.ts`, which owns the single `MutationObserver` over `document.body`: a callback
there is delivered once per microtask checkpoint, after a render batch is applied and before paint, so
it is both timelier and cheaper than polling. The other render-script dispatcher belongs to
`InfiniteList` and runs in its render pass instead — see below for why that difference matters.

Registration for all of them happens at **import time**, not on mount: the markup can be present in
the very first render, and registering when a component mounts would leave a window where the
mechanism is missing. That also means there is nothing to unregister — the set of names is fixed, and
the callbacks take everything they need as arguments rather than capturing a component or an element.

### Presence classes

Replaces `container:has(descendant)`. WebKit has no descendant-direction `:has()` bits, so it re-runs a
real match up the ancestor chain on every mutation — measured at 6-8% of WebContent's main thread
during a call. A presence class turns that into one class toggle per actual change.

Register in `app-presence-classes.ts`, never write the class in markup:

```typescript
MutationProcessor.registerPresenceClasses(
    { container: '.list-view-layout', match: '.audio-panel-header', className: 'has-audio-panel-header' },
);
```

Only **container subjects** belong here. A `:has()` whose subject is a small element (`.toggle`,
`.checkbox`, `.navbar-item`) walks a tiny subtree and costs nothing worth replacing.

### Animation sync

Phase-aligns looping CSS animations so they tick on shared instants. Left alone, each animation starts
when its element appears, so with enough of them nearly every frame contains a tick. On an iPhone 13
Pro, 8 unsynchronised looping animations cost +0.45 cores over idle and 40 cost +0.59 — the same 40
phase-aligned cost **+0.08**.

A component opts in by carrying a registered class; nothing calls anything. Add the class to
`animationClasses` in `animation-sync.ts`, and to `pseudoByClass` when the animation lives on a
pseudo-element (a pseudo's animation is invisible in the host's computed style, so it can't be
derived). Alignment back-dates `animation-delay`, so an element is in phase the moment it appears.

Only **stepped** animations benefit — a continuous one changes every frame regardless of phase, and
`sync` warns rather than pretending to help.

### Render scripts

Markup asks for a named script; the script is registered in JS. Two dispatchers, differing only in
when they fire:

| Attribute | Registered on | Fires |
|-----------|---------------|-------|
| `data-render-script-<name>` | `MutationProcessor` | from the DOM mutation, for anything outside a virtual list |
| `data-vl-render-script-<name>` | `InfiniteList` | inside the list's own render pass, on items and anything nested in one |

Both dedupe per element and name against the last value they ran with: a re-render that rewrites the
same value is not a re-run, and a changed value is.

The list variant exists because the mutation-driven one is **structurally too late** to change how a
render behaves. `InfiniteList` calls `applyRender` synchronously with the DOM batch, so by the time an
observer sees the new markup the render's decisions are already made. Measured on expanding a
conversation block:

```
click -> mutation callback (no items yet) -> beginAppearance x16 -> mutation callback (381 items)
```

Scanning inside `applyRender` puts the script ahead of that — `click -> renderScript ->
beginAppearance`. Use the list variant whenever the script must affect the render it arrives with; use
the `MutationProcessor` one otherwise.

**Caveat:** a render script is held for the lifetime of the page. Never register a name that varies per
instance (`quiet-heights-${chatId}`), and never capture a component, list or element in the callback —
both turn a fixed registry into an unbounded one that pins whatever it holds.

An example of the pair in use is the conversation collapse — see
[The virtual list](./virtual-list.md) §3.10.

## Tooltips

One `TooltipHost` serves the whole page, driven entirely from `data-*` attributes. The standard
buttons (`Button`, `ButtonRound`, `ButtonSquare`, `HeaderButton`) render the first three from their
`Tooltip`, `TooltipPosition` and `TooltipSeverity` parameters; any other element can write them
directly.

| Attribute | Meaning |
|-----------|---------|
| `data-tooltip` | The text. Resolved by walking up from the hovered node, so it can sit on a wrapper. |
| `data-tooltip-position` | A `FloatingPosition`; defaults to `top`. |
| `data-tooltip-severity` | `error` paints the pink/red variant. Absent or empty is the default look. |
| `data-render-script-tooltip-auto-show` | Non-empty shows that text with no pointer involved; empty clears it. |
| `data-tooltip-auto-show-duration` | Milliseconds for the auto-shown tooltip. Absent picks the default for the severity — 3s normal, 10s error; an explicit `0` keeps it up until the value clears. |

Auto-show is the only path that reaches a touch device, and it is a [render script](#render-scripts):
its value is the text, so a changed message re-triggers it while a re-render carrying the same one
does not. Hover always wins for display, and an auto-shown tooltip re-asserts itself once the pointer
leaves rather than being cancelled by a passing hover. A shown tooltip tracks its trigger's
attributes, so text and severity stay right even while the pointer never moves.

Prefer it over a component-local tooltip: two tooltip systems over one control overlap, because
pressing a control on a desktop also hovers it. `RecorderToggle` is the worked example — the record
button auto-shows only a recording failure, and never the healthy path. What counts as a failure is
`ChatAudioUI.GetRecordingStatus`, a compute method, so every recording control can report the same
thing. A problem the pipeline *named* — `RecorderStartResult`, carried up from `getUserMedia`'s
DOMException name, Android's `AudioRecord` state or Windows' `AudioDeviceNodeCreationStatus` — is a
verdict and is reported at once. An unnamed one is judged by age instead: it stays
`Starting`/`Reconnecting` until it outlasts `Constants.Audio.RecordingProblemGracePeriod`, measured
from the later of the press and the last recorder-pipeline transition, because a healthy start
passes through the same states on its way up.

## Modal Components

### Structure

Modal components implement `IModalView<T>` with a nested `Model` record:

```csharp
@implements IModalView<AmazingModal.Model>

<DialogFrame Class="amazing-modal" Title="Amazing" HasCloseButton="true">
    <Body>...</Body>
    <Buttons>...</Buttons>
</DialogFrame>

@code {
    [CascadingParameter] public Modal Modal { get; set; } = null!;
    [Parameter] public Model ModalModel { get; set; } = null!;

    private void CloseModal() => Modal.Close();

    public sealed record Model(string SomeParam);
}
```

### Registration

Every modal must be registered in `BlazorUIAppModule.cs`:

```csharp
services.AddTypeMap<IModalView>(map => map
    .Add<AmazingModal.Model, AmazingModal>()
);
```

### CSS Scoping

The `Class` parameter on `DialogFrame` becomes the CSS scoping class. Use it to scope all styles:

```css
.amazing-modal .dialog-body { }
.amazing-modal .c-content { }
```

### Opening Modals

```csharp
await Hub.ModalUI.Show(new AmazingModal.Model("value")).ConfigureAwait(true);
```

### Registration Checklist

When creating a new component with TS/CSS/Modal, register it in all required places:

| What | Where |
|------|-------|
| CSS file | `styles.css` — add `@import` |
| TypeScript file | `exports.ts` — add `export *` |
| Modal component | `BlazorUIAppModule.cs` — add `.Add<Model, Component>()` |

## See Also

- [Implementing Features](../development/implementing-features.md) — full-stack feature implementation guide
- [Coding Style](../CODING_STYLE.md) — general coding conventions
