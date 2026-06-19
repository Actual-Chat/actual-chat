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

Not all categories are present in every rule — include only what's needed. When a category has only one or two utilities, they can share a line. Separate comments are optional.

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

- [Implementing Features](./implementing-features.md) — full-stack feature implementation guide
- [Coding Style](../CODING_STYLE.md) — general coding conventions
