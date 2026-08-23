# ContentSwap — cross-fade swapping of a rendered region

## Goal

A generic Blazor wrapper that turns "the content of this region was replaced"
from a hard cut into an animated hand-off: the outgoing content is pinned on top
of the region at the size it already had, the incoming content builds underneath
it unseen, and the effect only runs once that content says it is there.

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
<ContentSwap LayerKey="@chatKey" Class="chat-view-swap">
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
public sealed partial class ContentSwap : ComponentBase<UIHub>, IAsyncDisposable
{
    [Parameter] public object? LayerKey { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Placeholder { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string LayerClass { get; set; } = "";
    [Parameter] public string Name { get; set; } = "";
    [Parameter] public ContentSwapEffect Effect { get; set; } = ContentSwapEffect.Fade;
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(0.5);
    [Parameter] public TimeSpan MaxDisplayDelay { get; set; } = TimeSpan.FromSeconds(0.333);
    [Parameter] public bool IsEnabled { get; set; } = true;
}
```

- **`LayerKey`** — the identity of what is currently rendered. A change starts a
  swap; anything else is an ordinary re-render. Compared with
  `Equals`. This parameter is unavoidable: a wrapper receives a fresh
  `RenderFragment` delegate on every parent render and cannot tell a swap from a
  re-render on its own. It is an ordinary parameter and has nothing to do with
  the `@key` directive — see [Instance identity](#instance-identity).
- **`ChildContent`** — the content for the current `LayerKey`. Rendered normally
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
- **`Name`** — optional; published as `data-swap-name` on the host so JS can
  address this area by name — see [The display signal](#the-display-signal).
- **`Effect`** — which of the predefined animations plays. One enum, read at
  the instant the swap starts rather than at render time, so a call site is free
  to pick a *direction* in the same parameter set that changes `LayerKey`.
  `ContentSwapEffect` maps to CSS classes on the host and nothing else, so
  `ContentSwap` still owns no timing function, no easing and no opacity — only
  when the classes appear and when the element is gone. See
  [Effects](#effects).
- **`Duration`** — how long an outgoing layer is kept, counted from the moment
  the effect is allowed to start. It is a **disposal deadline, not an animation
  length**: it has to be at least as long as the effect, and being longer only
  costs a dead subtree sitting invisible at `opacity: 0`. 0.5 s is comfortably
  above any of them.
- **`MaxDisplayDelay`** — the cap on the hold. Every swap holds: the outgoing
  layer stays fully opaque, occluding the region, until the incoming content
  says it is there, and this is how long that wait is allowed to last. It is a
  **backstop, not a tuning knob** — a call site that wants a different moment
  moves the signal, it doesn't move this. `TimeSpan.Zero` is the escape hatch
  that turns the hold off. See [The display signal](#the-display-signal).
- **`IsEnabled`** — `false` renders the current layer only; `LayerKey` changes are
  instant cuts. For turning the effect off per platform or per screen size
  without changing the markup. `Effect = None` with `MaxDisplayDelay` of zero
  does the same thing, and is what `None` degenerates to.

Only the current layer exists once a swap has finished, so at rest the component
costs one extra `<div>` and nothing else.

### Why there is no delay parameter

Two earlier drafts had one. The first was `FadeDelay`, a window where the
outgoing layer stays fully opaque so the new content can build unseen; it moved
into CSS as `--content-swap-hold` feeding `animation-delay`, on the argument that
the place that owns the curve should own the delay too.

Both are gone, because a delay of either kind is a *guess* at how long the
incoming content needs, tuned on one device against one trace. The content knows,
so the content says — see [The display signal](#the-display-signal) — and the
hold ends on that rather than on a clock. `MaxDisplayDelay` is what's left of the
guess, and it only matters when the signal never arrives.

This is also why no effect carries an `animation-delay` any more: the hold is a
*pause*, and a delay on top of a pause would postpone the effect past the moment
it exists to cover.

The other thing `ContentSwap` must know is `Duration`, and only because it — not
CSS — is what disposes the subtree.

### Instance identity

`ContentSwap` only works if **its own instance outlives the swap** — it is the
thing holding the outgoing layer alive. Two ways to break that, both of them
natural habits when wrapping existing markup:

1. **Putting `@key` on the wrapper.** `LayerKey` is a plain parameter, not the `@key`
   directive; the two are unrelated - which is why it is not called `Key`. Hoisting the existing
   `<ChatView @key="@chatKey"/>` up onto `<ContentSwap @key="@chatKey">` makes
   Blazor destroy and rebuild the wrapper on every change — today's hard cut,
   plus two extra `<div>`s. The only `@key` in the design is internal, on the
   layer elements, and it is a monotonic layer id rather than `LayerKey` so that
   `A → B → A` cannot reuse the still-dying `A` layer.
2. **Wrapping one branch of an `@if` chain.** The wrapper goes *outside* the
   whole chain, as in the example above. Inside a branch it is torn down
   whenever the branch changes — and branch-to-branch transitions
   (`ChatNotFound` → `ChatView` for the same chat) are exactly the ones worth
   cross-fading.

Point 2 has a corollary: when two branches share a `LayerKey`, there is no swap
between them. `chatContext` arriving late for a chat that first rendered as
`ChatNotFound` is the real case. If that transition should fade, `LayerKey` has to
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
private Layer? _outgoing;
private Layer _current = null!;

private IEnumerable<Layer> Layers()  // outgoing first — see below
{
    if (_outgoing is { } outgoing)
        yield return outgoing;

    yield return _current;
}
```

```razor
<div @ref="_ref" class="content-swap @Class @_effectClass"
     data-swap-hold="@_holdToken" data-swap-name="@_name">
    @foreach (var layer in Layers()) {
        var isOutgoing = layer == _outgoing;
        <div @key="@layer" class="c-layer @LayerClass @layer.CssClass" inert="@isOutgoing">
            <ContentSwapLayer Context="@layer.Context" Content="@layer.Content" IsFrozen="@isOutgoing"/>
        </div>
    }
</div>
```

- The `Layer` instance itself is the `@key`, not `LayerKey` — `A → B → A` in quick
  succession must not make Blazor reuse the dying `A` layer as the new one.
- `layer.CssClass` is `outgoing` for the layer on its way out and `incoming` for
  the current one while a swap is running; the effect classes go on the **host**,
  captured from `Effect` when the swap started. Changing `Effect` mid-swap must
  not restart the animation, and an effect that wants to move both layers needs
  to name them both from one place.
- Both layers render from **one `@foreach`**, so they are a single keyed sibling
  list. Two separate markup blocks would put them in different diff regions and
  the reorder could come out as a DOM move, which resets CSS animations and can
  reset scroll offsets on the very subtree we are trying to freeze.
- `ContentSwapLayer` is a three-parameter component whose whole job is
  `ShouldRender() => !IsFrozen` plus cascading its context. Frozen, it also
  ignores the incoming `Content` delegate, which is why the outgoing fragment is
  never re-invoked. `IsFrozen` is a plain parameter rather than something read
  off the context: freezing starts at the swap, while the context's states turn
  at the hand-off, and conflating two different moments in one flag is what the
  earlier `IsFading` did wrong.
- The layer wrapper `<div>` is rendered by `ContentSwap`, not by
  `ContentSwapLayer`, so `outgoing` and `inert` can be added without
  re-rendering the frozen subtree.
- We do **not** reuse `IsolateRerender` for the freeze even though it implements
  the same trick: it cascades itself, and a nested instance would capture the
  `RerenderRegion` in `DefaultLayout`.

The host also carries a JS counterpart, created once per instance in
`OnAfterRenderAsync(firstRender)` and holding a `DotNetObjectReference`. It is
the only interop in the component, and it exists so that the display signal —
which JS acts on first, before paint — reaches .NET at all. See
[The display signal](#the-display-signal).

### Why the outgoing layer goes first

Outgoing-first, current-second — the opposite of what paint order wants — for two
independent reasons.

**It is the only ordering that never asks Blazor to move a node.** A swap is
`[L1] → [L1, L2]`, an append. The drop at the end is `[L1, L2] → [L2]`, removing
the first child, which doesn't move the second. Current-first would need
`[L1] → [L2, L1]` — a prepend if Blazor is kind, a permutation if it isn't, and a
permutation detaches and re-attaches the very subtree being preserved.

**It puts the outgoing subtree ahead of the incoming one in every ordered
traversal**, which is the cheap way to keep the shared-area hand-off ordered.

The cost is that the current layer would paint over the outgoing one, so paint
order is restored explicitly with `z-index: 1` on `.c-layer.outgoing`. Grid items
honor `z-index` without `position`, so that is the whole fix.

## The layer state

The `RenderIntoStack` collision is one instance of a general problem: a subtree
that is on screen but on its way out still holds every registration it made in a
*shared* area. `RenderIntoSlot` and `RenderIntoStack` are the obvious ones,
`SettingsPanel.RegisterTab` is a third, and nothing stops the next one from being
added without anybody remembering this document. `ContentSwap` cannot enumerate
them — it has no idea what its content registers.

So the contract is inverted: **`ContentSwap` publishes where each layer is, and
anything that registers into a shared area is responsible for honoring it.**

```csharp
public enum ContentSwapLayerState
{
    Pending = 0, // Rendering underneath the layer that's on screen, waiting for its turn
    Displayed,   // What the user sees, and the only state that both renders and owns shared areas
    Replacing,   // Still on screen and still owns them, but frozen: the swap replacing it started
    Replaced,    // The next layer took over; this one is animating out and about to be disposed
}

public sealed class ContentSwapContext
{
    public ContentSwapLayerState State { get; private set; }
    public bool IsVisible => State.IsVisible();  // Displayed or Replacing
    public bool CanRender => State.CanRender();  // Pending or Displayed
    public event Action? StateChanged;
}
```

The two questions overlap on `Displayed` and nowhere else: a layer renders while it is
building or live, and owns its shared registrations while it is live or on its way
out. `Replacing` is the state that separates them - frozen, so its DOM, scroll
offsets and JS instances survive the hold untouched, but still what the user sees,
so it keeps the header, the sub-header stack and the tab rail until the hand-off.

The progression is strictly one-way. The swap moves the outgoing layer to
`Replacing`; the hand-off then moves it to `Replaced` and the incoming one to
`Displayed`, in that order, in the same call. `SetState` ignores a
step backwards, which is what lets `ContentSwap` issue those two calls without
its callers having to reason about their order.

**Why one enum and not two flags.** An earlier draft had `IsFading` +
`FadingStarted`; the obvious extension was a second `IsRevealed` / `IsHidden`
pair. Two booleans admit a fourth combination that cannot occur, so every
consumer has to not think about it, and two events mean two subscriptions whose
order matters. One event plus one question — `IsVisible` — is what every
consumer actually wants. The member names matter too: a `Pending` layer is *also*
hidden, so calling only the third state `Hidden` invites exactly the wrong
reading, where `Replaced` says why the layer stopped being live.

Cascaded per layer with `IsFixed="true"`. That matters: the value is a stable
instance created with the layer and never replaced, so the cascade costs nothing
on re-render — no change detection, no subscriber list — and consumers learn
about a transition through the event rather than through a render. Which is the
whole point, because **the outgoing layer never re-renders.**

The consumer side is small and identical in all four places:

```csharp
[CascadingParameter] private ContentSwapContext? Swap { get; set; }

protected override void OnInitialized() {
    if (Swap is { } swap)
        swap.StateChanged += OnStateChanged;
}

private void OnStateChanged() {
    if (Swap!.IsVisible)
        Register();
    else
        Unregister();
}
```

with the render path guarded by `if (Swap is { IsVisible: false }) return;`, so
a layer that hasn't had its turn yet never registers in the first place, and one
that has been replaced gives its registration up without re-rendering.

### Ordering within the hand-off

`ContentSwap` moves the outgoing layer to `Replaced` **before** it moves the
incoming one to `Displayed`, so a shared area never has two claimants, and
`RenderStack`'s first-wins dedup never sees a collision — no change to
`RenderStack` needed. `RenderSlot` takes the last registrant, so it would have
survived either order; the stack and the tab registry would not.

The residue is a moment where a shared area has *no* claimant. Blazor coalesces
`StateHasChanged` for a component that hasn't rendered yet, so in the normal case
`RenderStack` renders once, after both edits, and never sees the gap. If the
render queue happens to process it in between, a sub-header collapses to zero
height for one frame — the same failure `ui-components.md` documents under
"Children may not re-render in lock-step", with the same cheap fix (`min-height`
on the container).

Note what this timing is *not*: the hand-off happens at the display signal, not
at the start of the swap. During the hold the old content is still what the user
sees, so it is still what owns the header, the sub-header stack and the tab rail.
That is the property that would let the chat header and footer move inside the
swap area later and change over together with the body.

### Nesting

A consumer binds to the nearest `ContentSwapContext`, so a `ContentSwap` inside
another `ContentSwap`'s layer would shadow the outer one. Areas do nest — a panel
that swaps as a whole, with a tab body that swaps inside it — so the context
chains:

```csharp
private void Update()
{
    var state = _parent is null || _parent.State == ContentSwapLayerState.Displayed
        ? _ownState
        : _parent.State;
    ...
}
```

A layer is displayed only while the layer it sits in is; an outer layer that has
been replaced reports `Replaced` for everything inside it, displayed or not, so
nothing nested keeps a registration its container already gave up.

`ContentSwap` takes the enclosing context as a `[CascadingParameter]` and hands
it to every layer it creates. The subscription is the parent holding a reference
to the child, so a layer that comes and goes while the parent stays would pile up
there; `Detach()` — called wherever `ContentSwap` drops a layer, and from
`DisposeAsync` — is what keeps that bounded.

Nothing else about nesting needs handling: every CSS selector is `>` from
`.content-swap`, so an outer effect cannot reach an inner layer, and on the JS
side a display marker stops at the area it sits in.

### Swap

A `LayerKey` change is the whole algorithm:

```csharp
_effectClass = Effect.GetCssClass(); // read now, so a call site can pick it with LayerKey
if (_outgoing is { } lastOutgoing) { // a previous outgoing layer, if any, is simply dropped
    lastOutgoing.Context.SetState(ContentSwapLayerState.Replaced);
    lastOutgoing.Context.Detach();
}
outgoing.Context.SetState(ContentSwapLayerState.Displayed);
_outgoing = outgoing;                // whatever is on screen becomes the outgoing layer
outgoing.CssClass = "outgoing";
_current.CssClass = "incoming";
_holdToken = (++_swapIndex).Format();
_animator ??= new ComponentAnimator(this, Duration);
_animator.BeginAnimation(MaxDisplayDelay);
```

The `Layer` that `_outgoing` held before is not kept, not stacked, not
transitioned out — it is dropped in the same render batch and disposed. So a swap
during a swap **switches to a new pair**, and live subtrees are capped at two no
matter how fast the user clicks through chats. Note the `Displayed` call on the
new outgoing layer: mid-hold it was the one still building, and dropping the
layer above it makes it what the user sees, so it inherits the shared areas the
dropped one had. The visible cost is a pop when the dropped layer was still
opaque; the alternative is an unbounded stack of live virtual lists, which is not
a trade worth making.

`_animator` is a `ComponentAnimator` (`Components/Animation/`), which already
implements exactly "re-render this component once after a duration, cancelling
any previous pending one". It is armed twice per swap: first for
`MaxDisplayDelay`, whose firing is the backstop hand-off, then for `Duration`,
whose firing removes the outgoing subtree from the render tree and disposes it.
`OnDisplayed` arriving from JS re-arms it early, which is the normal path — and
the reason .NET is told at all, since otherwise every swap would pay the full
`MaxDisplayDelay + Duration` before disposing a dead `ChatView`.

### Timeline

`LayerKey` changes at t=0, `Effect = Fade`, `Duration = 0.5s`,
`MaxDisplayDelay = 0.333s`, content that says it is there at 120 ms:

| t | What happens |
|---|---|
| 0 | `_outgoing` = the old layer, frozen and marked `outgoing`; `_current` = a new layer, marked `incoming`; the host carries the effect and `data-swap-hold`. One render batch, both layers paused at time 0. |
| 0 – 120 ms | The new content renders, fetches its first tile and settles underneath, invisible. |
| 120 ms | `<ContentSwapDisplay/>` or a list's `displayContentSwap()` lands. JS drops the attribute before paint and the effect starts; .NET follows a frame later, moves the two layers to `Replaced` / `Displayed`, and arms `Duration`. |
| 120 – 370 ms | The effect runs. |
| 620 ms | `_outgoing` cleared, subtree removed and disposed. |

If nothing ever signals, the same swap holds to 333 ms and the rest shifts by
that much. The gap between the end of the effect and the disposal is deliberate
slack: an invisible `opacity: 0` subtree costs nothing to keep, while a disposal
that fires one frame early is a visible flash.

An intermediate re-render (very likely — `ChatPage` re-renders often) must not
disturb the effect. It doesn't: the outgoing layer's `class` string is recomputed
identically, so Blazor writes nothing, and a CSS animation that isn't re-declared
keeps running.

## CSS

`src/dotnet/UI.Blazor/Components/ContentSwap/content-swap.css`, registered in
`src/dotnet/UI.Blazor/styles.css`.

Three things, kept apart on purpose: the **structure**, which every call site
gets; the **hold**, which gates all of it; and one block per **effect**.

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
    background-color: var(--content-swap-bg, var(--background-01));
    @apply pointer-events-none;
    contain: layout paint style;
}

/* The hold — see The display signal */

.content-swap[data-swap-hold] > .c-layer,
.content-swap[data-swap-hold] > .c-layer::after {
    animation-play-state: paused !important;
}

/* One effect */

.content-swap.c-swap-fade > .c-layer.outgoing {
    will-change: opacity;
    animation: content-swap-fade-out
        var(--content-swap-fade, 0.25s) ease-out forwards;
}
```

Grid stacking rather than `position: absolute` so that adding and removing a
layer changes no box on either layer, and so a content-sized host still works.
The `z-index` restores paint order over the outgoing-first DOM order — grid items
honor `z-index` while still `position: static`.

The outgoing layer needs a **background of its own**. Most content is
transparent and sits on a surface far up the tree, so without one the swap is a
double exposure of both views rather than a hand-off — and a hold would show
nothing at all. `--content-swap-bg` is how a call site names the surface its
content actually sits on.

Per [`ui-components.md` → Animation Performance](../development/ui-components.md#animation-performance):
`opacity` and `transform` are level 1 and composited, and `will-change` /
`contain` sit on the animated element and only while it animates — the outgoing
layer is a transient extra compositor layer, not a permanent one. `Swipe` stays
on that tier - it is a pure transform. `Recede`, `Reveal` and the wipes leave it
(an animated blur radius, a backdrop filter, a mask sweep); `Fade` and `Defocus`
are the composited fallbacks if a device can't take them.

CSS **animations** rather than transitions: the placeholder layer is inserted
already-outgoing on the first render, and a transition does not fire on a freshly
inserted element. It also makes the hold a one-line `animation-play-state`.

Timing is per call site, as `--content-swap-fade` / `--content-swap-wipe` /
`--content-swap-swipe` /
`--content-swap-blur` on the host, so picking an effect stays a one-word change.
No effect carries an `animation-delay`: the hold is the pause above. `Duration` is deliberately *not* among them: it is the
disposal deadline, not the effect length, and wiring the two together would
re-create the two-values-that-must-agree problem this design removes.

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
  `RenderIntoSlot` / `RenderIntoStack` gain a `ContentSwapContext` cascading
  parameter and reuse their existing `Dispose` body as the un-register half of
  their state handler.
- `MutationProcessor.registerRenderScript` (`src/nodejs/src/mutation-processor.ts`)
  for the display signal. It already turns `data-render-script-<name>`
  appearing anywhere into a callback, delivered after a render batch and before
  paint, off the one observer the app has — which is exactly the shape the
  signal needs, and the reason there is no interop and no per-instance JS.

**Placement:** `ActualChat.UI.Blazor` (shared, no server/app deps), not
`UI.Blazor.App` — it is a generic layout primitive and three of its four
first consumers already live in the shared project or below it.

**Not added:** no JS interop and no DI service registration (per
`CODING_STYLE.md` rule 14, and because startup cost is paid on WASM/MAUI). The
one TypeScript file, `content-swap.ts`, holds no state and registers one render
script at import time. It holds a DotNetObjectReference per host, which is the
component's only interop: the display signal has to be observed in the DOM, which
is not somewhere C# can watch, and .NET has to hear about it to hand the shared
areas over and to stop holding the outgoing subtree.

## Hazards and companion fixes

These come from the outgoing subtree staying alive, and are the reason this plan
is worth agreeing on before writing code.

1. **Registrations into shared areas — resolved by
   [the layer state](#the-layer-state).** `RenderIntoSlot`, `RenderIntoStack`
   and `SettingsPanel.RegisterTab` are the three that exist today; the rule for
   anything added later is that a component which registers itself into an area
   it does not own must honor `ContentSwapContext.IsVisible`.
   Consumers updated in rollout step 1: `RenderIntoSlot`, `RenderIntoStack`.

2. **Keyboard shortcuts.** `ChatView` renders `[aria-keyshortcuts="pageup"]`
   buttons; two live copies mean two matches, and with outgoing-first DOM order
   the *dying* one is found first by a forward `querySelector`. Layers carry
   `inert`, so the fix is for keyux and `keyboard-ui.ts` (which uses `findLast`
   for Escape) to skip elements inside `[inert]` — correct behavior in general,
   not a workaround. These are plain markup rather than components, so the
   layer state cannot reach them.

3. **The dying subtree keeps working.** Its `InfiniteList` still holds
   observers, still calls `GetData`, still reports item visibility (and
   therefore read positions) for its chat. Harmless for one `Duration`, and the
   freeze means it is not *rendering* — but with `Duration` now a comfortable
   0.5 s rather than a tight fade length, this is worth measuring on Android
   WebView rather than assuming.

4. **Descendants can still self-re-render.** The freeze stops the parent's
   render from crossing the boundary; it cannot stop a `ComputedStateComponent`
   inside the outgoing layer from re-rendering itself when its own state
   invalidates. Behind an opaque outgoing layer this is invisible, only slightly
   wasteful.

5. **Two `OnUIEvent` subscriptions.** `NavigateToChatEntryEvent` would be
   handled by both `ChatView`s. The old one navigating within itself is
   invisible, but worth a look when wiring `ChatView` up.

## The display signal

A CSS delay is a guess about how long the incoming content needs, tuned on one
device against one trace. The case it is really standing in for is the plainest
one there is: **do not show the region rebuilding itself.** A keyed region tears
down, renders empty boxes or skeletons, fetches, re-measures and only then
settles — and every step of that is on screen. So every swap holds: the outgoing
layer stays fully opaque, occluding the region, and the effect only starts once
the incoming content says it is there.

| | |
|---|---|
| `ContentSwap` renders | `data-swap-hold="<n>"` on the host, unique per swap |
| CSS | `animation-play-state: paused` on both layers while that attribute is there |
| `<ContentSwapDisplay/>` renders | `data-render-script-content-swap-display` on a hidden `div` |
| `content-swap.ts` | removes `data-swap-hold` from every `.content-swap` above that element, then calls back into .NET |
| `ContentSwap.OnDisplayed` | `Pending → Displayed`, `Displayed → Replaced`, and the disposal clock starts |
| `ContentSwap` at `MaxDisplayDelay` | does the same, as the backstop |

Five things make this smaller than it looks:

- **The hold is a pause, not a withheld animation.** Every effect is declared the
  same way whether the swap is held or not; a paused animation sits at time 0,
  which for all of them is the state the layer already has, and runs from the
  start once the attribute goes. Adding an effect costs one rule, not two.
  (It does take `!important`: the `animation` shorthand each effect uses resets
  `animation-play-state`, and every effect rule out-specifies the gate.)
- **The visible half never round-trips.** `MutationProcessor` delivers its
  callback after a render batch is applied and before paint, so a hold ended
  there is one the user never saw as a delay. The `.NET` notification that
  follows is a frame late and does not need to be anything else — it drives the
  registration hand-off and the disposal clock, not the pixels.
- **JS keeps no state, and a marker speaks only for its own area.** `display()`
  clears the attribute on the nearest enclosing `.content-swap` and stops there;
  clearing it on an area that is not holding is a no-op, and only an area that
  *was* holding notifies .NET. It deliberately does not walk further up: an outer
  area may be waiting for something that lands later, and releasing it by proxy
  would end its hold early. Each area gets its own marker.
  `ui.ContentSwap.display('<Name>')` addresses one area directly instead, for a
  call site that has no element to point at.
- **Nothing can put the attribute back.** Blazor writes an attribute only when
  its *own* value for it changed, so a re-render mid-hold leaves the hand-off
  alone; and because the value is unique per swap, the next swap does re-arm it.
- **The signal is a contract, not an option.** Content that renders no
  `<ContentSwapDisplay/>` and no list that declares `IsContentSwapDependency` will
  sit out the full `MaxDisplayDelay` on every swap. That is the one way to get
  this wrong, and it is why the default is 0.333 s rather than something
  generous.

### Where the signal comes from

Two shapes, and they cover every call site on the branch:

- **`<ContentSwapDisplay/>`**, parameterless, rendered by content that considers
  itself on screen. Content that is ready immediately renders it in its first
  render — `NoChatSelected`, `ChatNotFound`, a settings tab body, the navbar's
  test-page list; content that loads renders it when it's done, and the call
  site's own `@if` is what expresses "when".
- **`VirtualList.IsContentSwapDependency`**, an opt-in `bool` that becomes
  `data-content-swap-dependency` on the list root. `virtual-list.ts` reads it in
  `displayContentSwap()`, which the two lists call from their *content-placed*
  paths only: `InfiniteList` when `isContentPlaced()` says the chain is
  positioned, `FiniteList` when it has rows or has confirmed itself empty.

The second one is why `ContentSwap` knows nothing about lists. A list is simply
one of the things that can say "I'm there", and the coupling points from the
list at `ContentSwap`, never back.

Deliberately **not** hooked up to `VirtualList.reveal()`, which is the list's own
un-hide: `FiniteList` un-hides on its very first render, skeletons and all, and
`InfiniteList` un-hides on a 1.5 s timeout when placement never happens. Letting
either end a hold would hand the backstop to the list and take it away from
`MaxDisplayDelay`.

### When the cap wins

`MaxDisplayDelay` is a promise to the user — never wait longer than this — so
when it runs out the swap goes ahead over content that is still building, and
the blink comes back later rather than not at all. Measured on
`/test/content-swap`, a list whose fake load is 0.6 s against a 0.4 s cap: old
content held to 407 ms, then a skeleton on screen until 609 ms.

Two things follow, and the second one is easy to get wrong:

- **Pick the cap from how long the content actually takes**, not from how long a
  hold feels acceptable. A cap below the content's typical time converts every
  swap into "hold, then blink", which is worse than the plain effect.
- **`None` is the worst effect to pair with a cap.** It cuts instantly, so when
  the cap wins it cuts instantly to a skeleton. Any effect with a real duration
  spends that duration on top of the cap and often covers the rest of the build
  — a 0.4 s cap with a 0.2 s wipe hides ~0.6 s, and degrades into an ordinary
  swap instead of a delayed blink.

### Effects

`ContentSwapEffect`, one enum, read at the instant the swap starts. Every member
maps to classes on the host and to nothing else:

| Member | What it does | Incoming layer | Length |
|---|---|---|---|
| `None` | An instant cut at the hand-off — the hold is what makes it a swap | — | 1 ms |
| `Fade` | Opacity, composited | — | `--content-swap-fade`, 0.2 s |
| `Defocus` | Fade behind a fixed blur | — | `--content-swap-fade`, 0.2 s |
| `Reveal` | Fade, with the incoming content blurred through it | — | `--content-swap-fade`, 0.2 s |
| `Recede` | Fade + shrink + growing blur | advances into place | `--content-swap-fade`, 0.2 s |
| `WipeRight` / `WipeLeft` / `WipeDown` / `WipeUp` | A dissolve front sweeps that way | stands still | `--content-swap-wipe`, 0.2 s |
| `SwipeRight` / `SwipeLeft` / `SwipeDown` / `SwipeUp` | Both layers slide that way as one | pushes the outgoing one out | `--content-swap-swipe`, 0.2 s |

Every effect runs 0.2 s, and `None` is the one exception - it is a cut, and the
1 ms animation exists only so the hold has something to pause. No call site
overrides any of the three variables, which are kept apart so one family can be
retuned without moving the others.

`Duration` is a separate number and has to be at least the effect length, because
it is what disposes the outgoing subtree. Nothing enforces that: `ChatPage` runs
`Duration = 0.15 s`, which is right for its `None` and would cut any real effect
off mid-animation if the call site changed without the deadline changing with it.
Every other call site is on the 0.5 s default, comfortably above all of them.

Under `prefers-reduced-motion` every effect is clamped to 1 ms, so the hold still
happens and the hand-off is a cut.

The four wipes are one implementation and four variables — the gradient angle,
which axis the mask is 3x on, and where the slide starts and ends — and the four
swipes are one implementation and two, the outgoing and incoming translations. A
direction is not a new effect. Reading the effect at swap time rather than at
render time is what lets a call site pick the direction *from* the change:
"the list moved down, so sweep down."

**Wipe vs. swipe.** A wipe dissolves the outgoing layer away in place, and the
incoming content is simply what was underneath - standing still the whole time. A
swipe moves both layers a full box the same way, so the incoming content pushes
the outgoing one out and the region reads as a strip being scrolled. The swipe
needs the host clipped for the length of the effect, since a layer at
`translateX(100%)` is entirely outside the region — `clip-path: inset(0)` rather than `overflow: hidden`, which
would make the host a scroll container.

The wipe is a single mask sweep. An earlier version rode a `backdrop-filter` blur
band on the same sweep through an `::after`, which was the expensive half of the
effect - a full-size re-sample of what sits behind the layer - and it was dropped
after looking at both side by side. Nothing in this component animates a
pseudo-element any more.

## Rollout

Shipped on `feat/content-swap`, one commit per step so any of them can be
reverted alone.

1. **The component.** `ContentSwap` + `ContentSwapLayer` + `ContentSwapContext`
   + `content-swap.css`, the `RenderIntoSlot` / `RenderIntoStack` consumers of
   the layer state, and `/test/content-swap`.
2. **`ChatView` in `ChatPage`.** Keyed by chat + branch.
3. **The navbar group.** Keyed by place / unread / test-pages, which covers
   place switching, all-chats, and chats ↔ notifications.
4. **The settings tab body.** Keyed by the selected tab.
5. **Effects and nesting.** `ContentSwapEffect` replacing the `FadeClass`
   string, the chained context, four wipe directions, and a
   `/test/content-swap` rebuilt around a scenario picker (chats / lists /
   nested) crossed with an effect picker.
6. **The display signal.** Every swap holds; `ContentSwapLayerState` replaces
   the fading flag; `<ContentSwapDisplay/>` and `VirtualList.IsContentSwapDependency`
   are what ends a hold; `MaxDisplayDelay` is the only backstop. `ChatPage`
   becomes `Effect = None`, the navbar `WipeUp`, and every CSS hold goes away.

Not migrated: `ChatHeader` / `ChatFooter`, `PlaceInfo`'s chat list, and the
`TabPanel` body. The header and footer cut at swap start rather than at the
hand-off — the simpler of the two options, and cheap to revisit: because
`RenderIntoSlot` renders its content at the *slot's* DOM position, moving the two
`RenderIntoSlot`s inside the existing `ContentSwap` never puts the swap host into
the `.layout-header > .c-content` chain that blocked this before, and
state-driven registration then hands them over together with the body.

### What the migrations taught

- **A hold is not optional for a heavy view.** Traced on a WASM chat switch,
  the incoming `InfiniteList` blocks the main thread for hundreds of ms, and an
  effect that starts immediately finishes while the new view is still mid-build.
  The first version guessed at that window with a CSS `animation-delay`; the
  signal replaced the guess, and the guess is what `MaxDisplayDelay` is now
  reduced to — a backstop nobody tunes.
- **A shared registration is not always a slot.** Three showed up that the
  original hazard list missed: `NotificationsPanelUI`'s open-at timestamp (a
  late `Close` from the outgoing widget would clobber the incoming widget's
  `Open`), and `SettingsPanel`'s tab registry, where a swap replaces every tab
  instance at once — both the duplicate-button problem the state solves, and
  `TabRegistry.ResolveSelectedTabId` deciding survival by instance, which had
  to become Id-based or every settings tab click would land on the last tab.
- **A collapsing host needs a rule.** Where the wrapped content was
  `display: none` in some state, the host stayed a flex item and kept claiming
  space — visible as the settings rail losing 123 of 420px on a narrow screen.
  The host has to mirror whatever hides its content.
- **A list's own un-hide is not the hand-off.** `FiniteList` un-hides on its
  first render, skeletons included, and `InfiniteList` un-hides on a 1.5 s
  timeout if placement never happens. Neither is "the content is there", so
  `displayContentSwap()` is a separate call made only from the content-placed
  path — otherwise the list's backstop would quietly replace `MaxDisplayDelay`.

## Open questions

1. **Does an incoming `transform` disturb a virtual list?** `Recede` and the
   wipes and swipes translate the incoming layer, and the whole layer moves as one, so
   relative geometry inside it is preserved. Anything measuring against the
   viewport rather than its own scroller would still see it.
2. **The remaining migrations** — the chat header and footer in particular,
   which cut at swap start while the body waits for the hand-off.
3. **Is `MaxDisplayDelay = 0.5 s` right for `ChatPage`?** It is the point where
   a slow chat stops being covered and starts blinking, and the only way to pick
   it is to measure real switches on a mid-range device.
