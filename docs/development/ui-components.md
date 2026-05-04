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

When adding a custom class to a button, use the `btn-` prefix followed by the purpose: `btn-rec`, `btn-save`, `btn-confirm`. Do not put `btn` at the end (`rec-btn`, `save-form-btn`).

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

### Respect `prefers-reduced-motion`

Decorative animations (film strips, placeholder waves) should be hidden when the user prefers reduced motion:

```css
@media (prefers-reduced-motion: reduce) {
    .c-film-strip {
        @apply hidden;
    }
}
```

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
