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

## See Also

- [Implementing Features](./implementing-features.md) — full-stack feature implementation guide
- [Coding Style](../CODING_STYLE.md) — general coding conventions
