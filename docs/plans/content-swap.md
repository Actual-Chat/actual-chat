# ContentSwap — cross-fade swapping of a rendered region

## Goal

A generic Blazor wrapper that turns "the content of this region was replaced"
from a hard cut into a cross-fade: the outgoing content is pinned on top of the
region at the size it already had and fades out, while the incoming content
renders underneath it and is revealed as the fade completes.

Target call sites, in order of value:

| Region | Today | Key |
|---|---|---|
| `ChatView` (`ChatPage.razor`) | `<ChatView @key="@chatKey"/>` — teardown + visible rebuild through skeletons | chat id |
| `ChatHeader` / `ChatFooter` slots (`ChatPage.razor`) | same cut, one render batch apart from the body | chat id |
| `ChatList` (`ChatListNavbarWidget`, `PlaceInfo`) | `<ChatList @key="@placeId"/>` | place id |
| `TabPanel` body (`TabContent.Invoke(SelectedTabId)`) | instant tab body swap | tab id |

## Why the current switch looks bad

`ChatView` is keyed by chat id, so a chat change disposes the whole subtree and
builds a new one. The new `InfiniteList` starts from `SkeletonCount="15"`
skeletons, fetches its first tile, re-measures, and only then settles — and the
first 500 ms of that are exactly the window `InfiniteList` now blocks its own
item-height animations for (`suspendHeightAnimationsUntil`, 6f9b2499c7). All of
that build-up is on screen. The same cut happens one batch later in the header
and footer, because those render through `RenderIntoSlot` and are re-rendered by
`RenderSlot` independently.

Nothing about that sequence is *wrong* — it just should not be visible.

## Mechanism

Three ways to keep the old pixels on screen while the new content builds. The
first one is what this plan proposes; the other two are recorded because they
are the obvious alternatives and both lose on the same axis.

### A. Two live layers (chosen)

The wrapper keeps the outgoing subtree **alive and in the DOM**, stacked over
the incoming one, and stops re-rendering it. No snapshotting, no measurement, no
JS at all.

- Both layers are grid-stacked into the same cell, so **the layer boxes never
  change** — nothing goes from static to absolute, no reflow at swap time, no
  `ResizeObserver` churn inside the dying virtual list.
- The outgoing layer is *frozen*: its wrapper component returns `false` from
  `ShouldRender()` forever after, so the parent's re-renders stop at that
  boundary and the old DOM is preserved byte-for-byte without re-invoking the
  old `RenderFragment`. This is the trick `IsolateRerender` already uses.
- Because the old fragment is never re-invoked, `ChildContent` can be an
  ordinary Razor child — no generic `RenderFragment<TKey>` gymnastics, and no
  stale-closure hazard.
- Scroll positions, `<canvas>` bitmaps, playing `<video>`, shadow roots of the
  `*.lit.ts` skeletons — all survive, because it is the real subtree.

The cost is that the outgoing subtree stays alive for the fade duration:
disposal is deferred, its JS instances and Fusion subscriptions keep running,
and its slot registrations are still in place. Those are addressed under
[Hazards](#hazards-and-companion-fixes); the freeze removes the largest part of
the cost, which is wasted render work while the new view is trying to render.

### B. Clone the old subtree into an inert ghost

`cloneNode(true)` the old layer into an overlay, let Blazor destroy the
original, fade the clone. Exactly one live subtree, so no duplicate services or
slot registrations.

Rejected: the snapshot has to be taken **before** the render batch that destroys
the old DOM, and synchronous JS interop only exists on WASM — MAUI's
`BlazorWebView` and Blazor Server are async-only. That forces a "render the old
content again, await an interop round-trip, then swap" dance, which delays the
new content by a frame or more. On top of that the clone needs manual
`scrollTop` restoration per scroller, re-upgrades every custom element, and pays
a full style+layout pass for a duplicate of the largest subtree in the app at
precisely the moment we want the render budget for the new one.

### C. View Transitions API

`document.startViewTransition()` does the rasterize-and-cross-fade natively and
is supported everywhere we ship. Rejected because its semantics are the opposite
of what is wanted here: the browser **suspends rendering** until the callback's
promise settles, then cross-fades two *static* snapshots. The new content would
be frozen at whatever it looked like when the transition started — skeletons —
and pop in afterwards. Scoping it to one element rather than the whole document
is also still not portable.

## API

`ActualChat.UI.Blazor.Components.ContentSwap`, in the shared `UI.Blazor`
project (no app or server dependencies), folder
`src/dotnet/UI.Blazor/Components/ContentSwap/`.

```razor
<ContentSwap Key="@chatKey" Class="chat-view-swap">
    <Placeholder>
        <chat-view-skeleton count="15" class="no-scrollbar"/>
    </Placeholder>
    <ChildContent>
        @if (m is null) {
            <chat-view-skeleton count="15" class="chat-view-skeleton-list no-scrollbar"/>
        } else if (chatContext is not null) {
            <CascadingValue Value="@chatContext">
                <ChatView/>
            </CascadingValue>
        } else if (route?.ChatId is not null) {
            <ChatNotFound ShowSignIn="@account.IsGuestOrNull()"/>
        } else {
            <NoChatSelected/>
        }
    </ChildContent>
</ContentSwap>
```

```csharp
public sealed partial class ContentSwap : ComponentBase, IDisposable
{
    [Parameter] public object? Key { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Placeholder { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string LayerClass { get; set; } = "";
    [Parameter] public string FadeClass { get; set; } = "content-swap-fade-out";
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(0.5);
    [Parameter] public bool IsEnabled { get; set; } = true;
}
```

- **`Key`** — the identity of what is currently rendered. A change starts a
  swap; anything else is an ordinary re-render. Compared with
  `Equals`. This parameter is unavoidable: a wrapper receives a fresh
  `RenderFragment` delegate on every parent render and cannot tell a swap from a
  re-render on its own. It is an ordinary parameter and has nothing to do with
  the `@key` directive — see [Instance identity](#instance-identity).
- **`ChildContent`** — the content for the current `Key`. Rendered normally
  while it is current; never re-invoked once it becomes outgoing.
- **`Placeholder`** — optional; rendered as an already-outgoing layer on the
  very first render, so the region starts as a skeleton that fades away while
  the real content builds behind it. Omit it and the first render is a plain
  render with no fade.
- **`Class`** — goes on the host element. The host must be given whatever
  sizing the content used to get from its old parent (see
  [Layout contract](#layout-contract)).
- **`LayerClass`** — goes on every layer element; an escape hatch for CSS that
  selects on the content's parent.
- **`FadeClass`** — added to a layer at the instant it becomes outgoing. This is
  the *entire* visual: `ContentSwap` owns no timing function, no easing, and no
  opacity — it only decides when the class appears and when the element is gone.
  The default `content-swap-fade-out` ships in `content-swap.css`; a call site
  that wants a slide, a scale, or a blur passes its own class instead.
- **`Duration`** — how long an outgoing layer is kept before it is removed from
  the render tree and its components disposed. It is a **disposal deadline, not
  an animation length**: it has to be at least as long as whatever `FadeClass`
  does, and being longer only costs a dead subtree sitting invisible at
  `opacity: 0`. 0.5 s is comfortably above any sane fade.
- **`IsEnabled`** — `false` renders the current layer only; `Key` changes are
  instant cuts. For turning the effect off per platform or per screen size
  without changing the markup.

Only the current layer exists once a swap has finished, so at rest the component
costs one extra `<div>` and nothing else.

### Why there is no delay parameter

An earlier draft had `FadeDelay` — a window where the outgoing layer stays fully
opaque so the new content can build unseen. It is gone, because with `FadeClass`
the caller already has it and more: `animation-delay` (or `transition-delay`, or
a `cubic-bezier` that simply holds near `opacity: 1` for its first third) all
express a hold, and they express it in the one place that also owns the curve.
Two parameters that must agree with each other are worse than one that can't
disagree.

The only thing `ContentSwap` must know is `Duration`, and only because it — not
CSS — is what disposes the subtree.

### Instance identity

`ContentSwap` only works if **its own instance outlives the swap** — it is the
thing holding the outgoing layer alive. Two ways to break that, both of them
natural habits when wrapping existing markup:

1. **Putting `@key` on the wrapper.** `Key` is a plain parameter, not the `@key`
   directive; the two are unrelated. Hoisting the existing
   `<ChatView @key="@chatKey"/>` up onto `<ContentSwap @key="@chatKey">` makes
   Blazor destroy and rebuild the wrapper on every change — today's hard cut,
   plus two extra `<div>`s. The only `@key` in the design is internal, on the
   layer elements, and it is a monotonic layer id rather than `Key` so that
   `A → B → A` cannot reuse the still-dying `A` layer.
2. **Wrapping one branch of an `@if` chain.** The wrapper goes *outside* the
   whole chain, as in the example above. Inside a branch it is torn down
   whenever the branch changes — and branch-to-branch transitions
   (`ChatNotFound` → `ChatView` for the same chat) are exactly the ones worth
   cross-fading.

Point 2 has a corollary: when two branches share a `Key`, there is no swap
between them. `chatContext` arriving late for a chat that first rendered as
`ChatNotFound` is the real case. If that transition should fade, `Key` has to
carry the branch as well as the chat id.

### Layout contract

The wrapper introduces two boxes (`.content-swap` host, `.c-layer` per layer)
between the old parent and the content, so:

1. **The host must occupy the box the content used to occupy.** For `ChatView`
   that means the host is the `.layout-body` flex child and gets
   `flex-1 min-h-0`; `.chat-view` keeps its `height: 100%` and now resolves it
   against the layer.
2. **Direct-child CSS selectors across the boundary break.** Any
   `.layout-body > .chat-view` style has to become a descendant selector or move
   onto `LayerClass`. (Checked: `.chat-view` has no such rule today; the
   `> ` chains in `main.css` are all *above* `.layout-body`.)
3. **The host's height during a swap is `max(old, new)`** when it is
   content-sized. Irrelevant for the four target call sites, all of which are
   sized by their parent.

Note that the content is *not* required to render a single root node — the
per-layer `<div>` is what gets positioned and faded, so any number of roots
works.

## Rendering and lifecycle

The whole state is a **pair**: one current layer, and at most one outgoing layer.
Never more, whatever the user does.

```csharp
private Layer? _fading;
private Layer _current = null!;

private IEnumerable<Layer> Layers()  // fading first — see below
{
    if (_fading is { } fading)
        yield return fading;

    yield return _current;
}
```

```razor
<div class="content-swap @Class">
    @foreach (var layer in Layers()) {
        <div @key="@layer.Id" class="c-layer @LayerClass @layer.CssClass" inert="@layer.IsFading">
            <CascadingValue Value="@layer.Context" IsFixed="true">
                <ContentSwapLayer Content="@layer.Content" IsFading="@layer.IsFading"/>
            </CascadingValue>
        </div>
    }
</div>
```

- Layers carry a monotonically increasing `Id` used as the `@key`, not `Key` —
  `A → B → A` in quick succession must not make Blazor reuse the dying `A` layer
  as the new one.
- `layer.CssClass` is `""` for the current layer and `$"outgoing {FadeClass}"`
  for the fading one, **captured when it started fading**. Changing `FadeClass`
  mid-fade must not restart the animation.
- Both layers render from **one `@foreach`**, so they are a single keyed sibling
  list. Two separate markup blocks would put them in different diff regions and
  the reorder could come out as a DOM move, which resets CSS animations and can
  reset scroll offsets on the very subtree we are trying to freeze.
- `ContentSwapLayer` is a two-parameter component whose whole job is
  `ShouldRender() => !IsFading` plus cascading its context. Once fading it also
  ignores the incoming `Content` delegate, which is why the outgoing fragment is
  never re-invoked.
- The layer wrapper `<div>` is rendered by `ContentSwap`, not by
  `ContentSwapLayer`, so `FadeClass` and `inert` can be added without
  re-rendering the frozen subtree.
- We do **not** reuse `IsolateRerender` for the freeze even though it implements
  the same trick: it cascades itself, and a nested instance would capture the
  `RerenderRegion` in `DefaultLayout`.

### Why the fading layer goes first

Fading-first, current-second — the opposite of what paint order wants — for two
independent reasons.

**It is the only ordering that never asks Blazor to move a node.** A swap is
`[L1] → [L1, L2]`, an append. The drop at the end is `[L1, L2] → [L2]`, removing
the first child, which doesn't move the second. Current-first would need
`[L1] → [L2, L1]` — a prepend if Blazor is kind, a permutation if it isn't, and a
permutation detaches and re-attaches the ghost.

**It puts the fading subtree ahead of the incoming one in every ordered
traversal** — which is what the fading signal below needs in order to vacate
shared areas before the new content claims them.

The cost is that the current layer would paint over the fading one, so paint
order is restored explicitly with `z-index: 1` on `.c-layer.outgoing`. Grid items
honor `z-index` without `position`, so that is the whole fix.

## The fading signal

The `RenderIntoStack` collision is one instance of a general problem: a subtree
that is on screen but dying still holds every registration it made in a *shared*
area. `RenderIntoSlot` and `RenderIntoStack` are the obvious ones,
`TabPanel.RegisterTab` is a third, and nothing stops the next one from being
added without anybody remembering this document. `ContentSwap` cannot enumerate
them — it has no idea what its content registers.

So the contract is inverted: **`ContentSwap` publishes the fact, and anything
that registers into a shared area is responsible for honoring it.**

```csharp
public sealed class ContentSwapLayerContext
{
    public bool IsFading { get; private set; }
    public event Action? FadingStarted;

    internal void StartFading() { ... }  // sets the flag, then raises the event once
}
```

Cascaded per layer with `IsFixed="true"`. That matters: the value is a stable
instance created with the layer and never replaced, so the cascade costs nothing
on re-render — no change detection, no subscriber list — and consumers learn
about the flip through the event rather than through a render. Which is the whole
point, because **the fading layer never re-renders.** A `CascadingValue` whose
*value* changed would also work (it notifies subscribers directly rather than
through the render walk), but only if `ContentSwap` renders it outside the frozen
boundary, and it would re-notify on every unrelated re-render.

The consumer side is small. For `RenderIntoSlot`, the fading handler does exactly
what `Dispose` already does, so it is the same private method:

```csharp
[CascadingParameter] private ContentSwapLayerContext? SwapLayer { get; set; }

protected override void OnInitialized() {
    if (SwapLayer is { IsFading: false } swapLayer)
        swapLayer.FadingStarted += Unregister;
}
```

`Unregister` removes the entry and calls `NotifyChanged()` — a slot re-render,
not a re-render of the frozen subtree. The registration path gets the matching
guard (`if (SwapLayer?.IsFading == true) return;`) so a component that somehow
initializes inside an already-fading layer never registers in the first place.

### Ordering within the batch

`ContentSwap` flips the flag in `OnParametersSet`, i.e. **before** it renders and
therefore before the incoming layer's content exists. So the fading subtree
always vacates a shared area before the new one claims it, and `RenderStack`'s
first-wins dedup never sees a collision — no change to `RenderStack` needed.

The residue is a moment where a shared area has *no* claimant. Blazor coalesces
`StateHasChanged` for a component that hasn't rendered yet, so in the normal case
`RenderStack` renders once, after both edits, and never sees the gap. If the
render queue happens to process it in between, a sub-header collapses to zero
height for one frame — the same failure `ui-components.md` documents under
"Children may not re-render in lock-step", with the same cheap fix (`min-height`
on the container). Worth watching for at rollout step 4, not worth pre-solving.

### Nesting

A consumer binds to the nearest `ContentSwapLayerContext`, so a `ContentSwap`
inside another `ContentSwap`'s layer would shadow the outer signal: the inner
layer's consumers would not hear the outer fade. None of the four call sites nest
today. The fix if one appears is a `Parent` link on the context, with `IsFading`
and the event chaining through it — and `ContentSwap` detaching the chain when it
drops a layer.

### Swap

A `Key` change is the whole algorithm:

```csharp
_fading = _current;                  // whatever is on screen becomes the ghost
_fading.StartFading(FadeClass);      // flag + event, before anything renders
_current = new Layer(ChildContent);  // a previous ghost, if any, is simply dropped
_animator.BeginAnimation(Duration);
```

The `Layer` that `_fading` held before is not kept, not stacked, not transitioned
out — it is dropped in the same render batch and disposed. So a swap during a
swap **switches to a new pair**, and live subtrees are capped at two no matter
how fast the user clicks through chats. The visible cost is a pop when the
dropped ghost was still opaque; the alternative is an unbounded stack of live
virtual lists, which is not a trade worth making.

This runs in `OnParametersSet`, so `StartFading` — and therefore every consumer's
unregistration — completes before the new layer is rendered.

`_animator` is a `ComponentAnimator` (`Components/Animation/`), which already
implements exactly "re-render this component once after a duration, cancelling
any previous pending one". Its callback clears `_fading`, which is what removes
the subtree from the render tree and disposes it.

### Timeline

`Key` changes at t=0, `Duration = 0.5s`, default `FadeClass`:

| t | What happens |
|---|---|
| 0 | `_fading` = the old layer, flagged and carrying `FadeClass`; `_current` = a new layer. One render batch. |
| 0 – 250 ms | The CSS in `FadeClass` runs. The new content renders, fetches its first tile and settles underneath. |
| 500 ms | `_fading` cleared, subtree removed and disposed. |

The gap between the end of the fade and the disposal is deliberate slack: an
invisible `opacity: 0` subtree costs nothing to keep, while a disposal that fires
one frame early is a visible flash.

An intermediate re-render (very likely — `ChatPage` re-renders often) must not
disturb the fade. It doesn't: the outgoing layer's `class` string is recomputed
identically, so Blazor writes nothing, and a CSS animation that isn't re-declared
keeps running.

## CSS

`src/dotnet/UI.Blazor/Components/ContentSwap/content-swap.css`, registered in
`src/dotnet/UI.Blazor/styles.css`.

Two things, kept apart on purpose: the **structure**, which every call site gets,
and the **default fade class**, which any call site may replace.

```css
/* Structure — stacking, and what "outgoing" means regardless of the effect */

.content-swap {
    @apply grid;
    grid-template-areas: "content-swap";
}
.content-swap > .c-layer {
    @apply min-w-0 min-h-0;
    grid-area: content-swap;
}
.content-swap > .c-layer.outgoing {
    @apply z-10;
    @apply pointer-events-none;
    will-change: opacity;
    contain: layout paint style;
}

/* Default FadeClass */

.content-swap-fade-out {
    animation: content-swap-fade-out 0.25s ease-out forwards;
}
@keyframes content-swap-fade-out {
    from { opacity: 1; }
    to { opacity: 0; }
}
@media (prefers-reduced-motion: reduce) {
    .content-swap-fade-out {
        animation-duration: 1ms;
    }
}
```

Grid stacking rather than `position: absolute` so that adding and removing a
layer changes no box on either layer, and so a content-sized host still works.
The `z-index` restores paint order over the fading-first DOM order — grid items
honor `z-index` while still `position: static`.

Per [`ui-components.md` → Animation Performance](../development/ui-components.md#animation-performance):
`opacity` only (level 1, composited), and `will-change` / `contain` sit on the
animated element and only while it animates — the outgoing layer is a transient
extra compositor layer, not a permanent one. A custom `FadeClass` inherits both,
and inherits the obligation to stay on composited properties.

A CSS **animation** rather than a transition, and a constraint on any
replacement class: the placeholder layer is inserted already-outgoing on the
first render, and a transition does not fire on a freshly inserted element. A
`FadeClass` built on `transition` will work for swaps and silently do nothing for
the placeholder.

`Duration` is deliberately *not* published as a CSS variable. It is the disposal
deadline, not the fade length; wiring the two together would make a 0.5 s fade
the default and re-create the two-values-that-must-agree problem the `FadeClass`
design removes.

## Reuse

**Existing abstractions this uses:**

- `ComponentAnimator` (`UI.Blazor/Components/Animation/ComponentAnimator.cs`) —
  the "re-render me once after N, cancelling the previous" timer, for dropping
  the outgoing layer. Its siblings `ShowHideAnimator` / `OnOffAnimator` were
  considered and don't fit: they model one element's class going through
  `off → off-to-on → on`, not a pair of layers.
- `IsolateRerender` / `RerenderRegion` — prior art for the `ShouldRender()`
  freeze; the pattern is reused, the type is not (see above).
- The existing `*.lit.ts` skeletons — `chat-view-skeleton`,
  `chat-list-skeleton`, `tab-skeleton` — are already exactly the right
  `Placeholder` content. No new skeleton work.
- `RenderSlot` / `RenderStack` for the header/footer regions, unchanged.
  `RenderIntoSlot` / `RenderIntoStack` gain a `ContentSwapLayerContext`
  cascading parameter and reuse their existing `Dispose` body as the
  fading handler.

**Placement:** `ActualChat.UI.Blazor` (shared, no server/app deps), not
`UI.Blazor.App` — it is a generic layout primitive and three of its four
first consumers already live in the shared project or below it.

**Not added:** no TypeScript, no JS interop, no DI service registration
(per `CODING_STYLE.md` rule 14, and because startup cost is paid on WASM/MAUI).

## Hazards and companion fixes

These come from the outgoing subtree staying alive, and are the reason this plan
is worth agreeing on before writing code.

1. **Registrations into shared areas — resolved by
   [the fading signal](#the-fading-signal).** `RenderIntoSlot`,
   `RenderIntoStack` and `TabPanel.RegisterTab` are the three that exist today;
   the rule for anything added later is that a component which registers itself
   into an area it does not own must honor `ContentSwapLayerContext.IsFading`.
   Consumers updated in rollout step 1: `RenderIntoSlot`, `RenderIntoStack`.

2. **Keyboard shortcuts.** `ChatView` renders `[aria-keyshortcuts="pageup"]`
   buttons; two live copies mean two matches, and with fading-first DOM order
   the *dying* one is found first by a forward `querySelector`. Layers carry
   `inert`, so the fix is for keyux and `keyboard-ui.ts` (which uses `findLast`
   for Escape) to skip elements inside `[inert]` — correct behavior in general,
   not a workaround. These are plain markup rather than components, so the
   fading signal cannot reach them.

3. **The dying subtree keeps working.** Its `InfiniteList` still holds
   observers, still calls `GetData`, still reports item visibility (and
   therefore read positions) for its chat. Harmless for one `Duration`, and the
   freeze means it is not *rendering* — but with `Duration` now a comfortable
   0.5 s rather than a tight fade length, this is worth measuring on Android
   WebView rather than assuming.

4. **Descendants can still self-re-render.** The freeze stops the parent's
   render from crossing the boundary; it cannot stop a `ComputedStateComponent`
   inside the outgoing layer from re-rendering itself when its own state
   invalidates. Behind a fading layer this is invisible, only slightly wasteful.

5. **Two `OnUIEvent` subscriptions.** `NavigateToChatEntryEvent` would be
   handled by both `ChatView`s. The old one navigating within itself is
   invisible, but worth a look when wiring `ChatView` up.

## Optional: readiness-driven fade

Whatever hold `FadeClass` encodes is a guess about how long the new content
needs. The principled version is to let the incoming content say when it is
ready: `ContentSwap` cascades a small context with `AddReadyBarrier(Task)`, and
`FadeClass` is applied only once every barrier has settled, clamped by a
`MaxHold`. `ChatView` would register the task that `InfiniteList` completes on
its first data render — the same shape as
`InfiniteList.suspendHeightAnimationsUntil`.

This composes cleanly with the `FadeClass` design, since "when is the class
applied" and "what does the class do" stay separate. Deferred anyway: it couples
the wrapper to its content, and a CSS-encoded hold should be tried first to see
whether the extra precision is even visible.

## Rollout

1. `ContentSwap` + `ContentSwapLayer` + `ContentSwapLayerContext` +
   `content-swap.css`, and the `RenderIntoSlot` / `RenderIntoStack` consumers of
   the fading signal.
2. `TabPanel` body — the smallest consumer, no virtual list, no slots. Proves
   the layout contract.
3. `ChatList` in `ChatListNavbarWidget` / `PlaceInfo` — adds a virtual list.
4. `ChatView` in `ChatPage`, together with the `ChatHeader` / `ChatFooter` slot
   contents, all keyed on the same chat id. Separate `ContentSwap` instances
   stay in sync because they all start on the same render batch and share a
   `FadeClass`. This is the step that exercises the fading signal and the
   `[inert]` keyboard fix.

Steps 2–4 are each independently revertable by removing one wrapper.

## Open questions

1. **Name.** `ContentSwap` throughout here; `FadeSwap`, `SwapView`,
   `CrossFade`, `ViewSwapper` are the alternatives.
2. **Scope of the first cut** — is `ChatView` + its header/footer the target, or
   should this land on `TabPanel` only until it has been looked at on a real
   Android device?
