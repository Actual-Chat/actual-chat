# Further rendering improvements during speech

Status: **R3 shipped** — `@keyframes rotate-ring` is transform-only now, and the
animation runs on `steps(25)` rather than `linear`. R4–R7 are open.

## Context

Continuation of the prior redraw / battery investigation on
`https://local.voxt.ai/chat`. After R1 (`content-visibility: auto` on
`streaming-entry-badge`) and R2 (`isVisible` gate on
`active-recording-svg`'s power pipeline) shipped, the page sustains
60 fps with 0 dropped frames during active recording on this desktop.

This plan lists the next tier of improvements — items that won't
necessarily move the desktop frame counter but will reduce continuous
CPU/GPU work during recording (so iOS Safari Energy panel and lower-end
Android stop spending budget on the recorder UI).

## Recommended changes (ordered by impact, lowest-risk first)

### R3 — `rotate-ring` recorder-pulse animation: animate transform only

`src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/chat-audio-panel.css:220-224`

```css
.chat-audio-panel .recorder-wrapper.record-on::before,
.chat-audio-panel .recorder-wrapper.record-off-to-on::before {
    opacity: 1;
    animation: rotate-ring 2.5s 1s linear infinite;
}
```

`@keyframes rotate-ring` (line 924) animates **`transform: rotate`** *and*
**`box-shadow`** (four directional offsets that morph at every keyframe).
The transform is GPU-composited; the multi-layer `box-shadow` morph is
**main-thread paint every frame, infinitely, while recording is on**.
This is the biggest steady-state continuous-paint cost during recording
that we haven't addressed.

The visual is a rotating ring with a small directional highlight. Two
options:

- **Option A (preferred):** keep the pseudo-element rotation
  (`transform: rotate`, composited) but remove the box-shadow morph.
  Replace with a static box-shadow (or `filter: drop-shadow`) that's
  uniform around the ring and rotates with it — visually similar, paints
  once per frame only on transform composition.
- **Option B:** if the directional highlight must be preserved, render
  the ring as an SVG `<circle>` with a stroke + a single rotating
  `<linearGradient>`. SVG gradient transforms are still composited.

Estimated win on iOS Safari: noticeable Energy panel drop while the
record button is on; the ring is non-trivially sized so its raster cost
is the largest continuous paint surface during recording.

### R4 — Structural-rebuild stabilization: streaming → not-streaming transition

`src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageInternalView/ChatEntryMessageInternalView.razor:65-115`

When a streaming entry finalizes, the rendered subtree changes shape:

- streaming branch: `<span retained>` + `<span changes>` (with N
  `change-item` spans staggered by `transition-delay`) + `<span>…@Tail</span>`
- from-streaming (transient): `<div temporary-container hide>` + `<MarkupView>`
- not-streaming: `<MarkupView>`

Blazor diff cannot preserve nodes across the structural change → it
removes all streaming spans, inserts the MarkupView spans. Each segment
finalize fires the burst we saw at sec 3 of the live trace (~150
mutations centered on `chat-message-markup`, `paragraph-markup`,
`message-wrapper`, `chat-message group`).

Approach: render a **single stable shell** (`<div class="chat-message-markup
…">`) with both the streaming spans AND the MarkupView always emitted
inside it, gated by `display: none` driven by `streamingCls` instead of
`@if`. Blazor sees the same component tree both before and after; the
transition becomes a class change. Only the first mount creates the
MarkupView; subsequent finalizes just unhide it.

Cost: medium. MarkupView runs its own ComputeState and `OnAfterRender`
JS init (`ChatEntryMessageInternalView.create`); making it always
render means the JS init runs on streaming entries too. That JS init
must therefore be idempotent w.r.t. streaming children.

Estimated win: removes the per-segment burst (~80–100 mutations clustered
per finalize) — the largest residual non-steady cost during dictation.

### R5 — Stale `streaming` lifecycle bug (anomaly #4)

Live capture during this session showed up to **9**
`.chat-message-markup.streaming` divs in DOM with old timestamps
(yesterday, hours-old). Some had "Transcribing" text + shimmer running
forever, others had empty text. None should be streaming. Each pays
the per-frame shimmer paint cost (now mitigated by R1's
`content-visibility: auto`, but still).

This is a data-lifecycle bug, not a render-perf bug. Investigation
entry-points:

- `ChatEntryMessageInternalView.OnInitialized` (line 183) caches a
  `TranscriptStreamReader` keyed by `_textEntryId`. If the cache
  retains a streaming reader past server-side completion, the
  client model will keep `IsStreaming` true.
- `IChats.GetEntry` server response — verify
  `entry.IsContentStreaming` clears when audio entry transcription
  completes.
- `TranscriptStreamReader.State` — check whether it forwards the
  server's "stream complete" signal.

Recommend: add a console warning when an entry older than e.g. 2
minutes still reports `IsContentStreaming = true`, run for one session
to localise where the flag persists, then fix.

### R6 — Lift `MessageHoverMenu` placeholder out of per-message tree

`src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor:215`

The `<MessageHoverMenu @ref="@_messageHoverMenu" .../>` is rendered
once per message, even though it produces no DOM until `MustShow=true`
(`MessageHoverMenu.razor:3`). Each ChatEntryMessageView re-render diffs
the empty placeholder. With ~75 messages in the virtual list and
list-level re-renders during finalize bursts, that's a measurable
fixed overhead.

Approach: mount one shared `MessageHoverMenu` host at the chat-view
root, parameterised by the currently-hovered entry. `ShowHoverMenu` /
`HideHoverMenu` set the active entry on the shared host instead of
toggling a per-message instance.

Cost: medium-large refactor. Touches every site that calls
`_messageHoverMenu?.Show()/Hide()`.

Estimated win: cuts the `_bl_*` Blazor instance churn we saw (24 unique
component IDs in 16 s) roughly in half.

### R7 — Per-word ChatList preview re-render — use targeted ShouldRender

Earlier R3 attempt (now reverted) tried to stabilise `LastTextEntry`
inside `ComputeState`, gained ~30 % reduction in `c-last-message` /
`c-text` mutations, hidden by ChatAudioState flap. The cleaner
formulation is at the ShouldRender level: short-circuit re-render
when the only delta is `LastTextEntry`'s version bump on a streaming
entry whose `Id` matches the previous render.

`src/dotnet/UI.Blazor.App/Components/ChatList/ChatListItem.razor:176-183`

```csharp
protected override bool ShouldRender() {
    if (_renderedModel == null) return true;
    var m = State.LastNonErrorValue;
    if (m == _renderedModel) return false;
    // Streaming-entry word bump: only LastTextEntry/LastTextEntryText changed,
    // and the entry is still streaming → no visible delta in this component.
    if (m.LastTextEntry is { IsContentStreaming: true } e1
        && _renderedModel.LastTextEntry is { IsContentStreaming: true } e0
        && e1.Id == e0.Id
        && (m with { LastTextEntry = e0, LastTextEntryText = _renderedModel.LastTextEntryText })
            == _renderedModel)
        return false;
    return true;
}
```

The `with` rebuild lets us compare "everything except text-streaming
fields". If the equality holds, we skip the render. Other fields
(`AudioState`, `UnreadCount`, `IsSelected`) propagate normally.

Cost: small. Local to ChatListItem.
Estimated win: ~30–50 % fewer `c-last-message` / `c-text` /
`c-author-header` mutations during dictation. Smaller than R3/R4 but
addresses real per-word work that the prior attempt only partially fixed.

## Out of scope / not worth doing

- `change-item` span restructuring — measured at 0.5/s, Blazor diff
  already minimises mutations, no measurable win.
- `contain: layout style` on `chat-message-markup` — A/B during
  recording showed no frame-time delta; layout cascade is already
  bounded by `.chat-view.virtual-list { contain: strict }`.
- `chat-activity-svg` opacity-only animation in chat list during
  streaming — composited, cheap.
- `pulse 2s` on recording-sub-header `.c-round` — opacity, composited.
- `record-btn-on-pulse 2s` on the on-state record button —
  `transform: scale`, composited.

## Critical files

- `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/chat-audio-panel.css`
  (R3 — `rotate-ring` keyframes lines 220-224, 924-965)
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageInternalView/ChatEntryMessageInternalView.razor`
  (R4 — render branch lines 65-115)
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageInternalView/chat-entry-message-internal-view.ts`
  (R4 — verify JS-init idempotency)
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageInternalView/ChatEntryMessageInternalView.razor:183-199`
  (R5 — TranscriptStreamReader cache lifecycle)
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor:215`
  (R6 — per-message MessageHoverMenu instantiation)
- `src/dotnet/UI.Blazor.App/Components/ChatList/ChatListItem.razor:176-183`
  (R7 — ShouldRender refinement)

## Verification

For each R-step, before/after:

1. Open `https://local.voxt.ai/chat`, start recording, dictate ~10 s
   while the chrome-devtools MCP runs the same A/B harness used in this
   session: 12 s window, RAF deltas + body-mutation observer with
   per-class buckets.
2. Compare totals: per-class mutation counts, frame-time p99 / max,
   drops > 20 ms.
3. R3 specifically: also stop recording and switch the page to
   `Performance → Rendering → Paint flashing` while recording is on;
   the rotating ring area should flash once per frame *only via
   compositor*, not as a paint rect.
4. R5: log entries with `IsContentStreaming` older than 2 minutes
   to console, observe whether they appear after a fresh page load
   (server-state issue) vs. accumulate during a session (client-state
   issue).
5. iOS Safari Energy panel (per the project memory entry on profiler
   measurement artefacts — Web Inspector Timeline + verbose console
   logs inflate readings 4×; use Energy panel only and quiet logs).
   Compare with R1+R2 baseline.

## Prior shipped work (context)

Already merged on `dev` ahead of this plan:

- Commit `6fa3bbd58` — `perf(chat): skip off-screen streaming-entry-badge
  paint via content-visibility` (R1).
- Commit `82ced7f5c` — `perf(audio): gate active-recording-svg power
  pipeline on isVisible` (R2).

Combined effect (verified via chrome-devtools MCP, A/B RAF timing during
active recording with live transcription):

| | pre-fix | post-fix |
|---|---|---|
| avg frame ms | 16.75 | 16.66 |
| p95 ms | 16.90 | 16.80 |
| max ms | 33.50 | 17.00 |
| drops > 20 ms | 1 / 4 s | 0 / 12 s |

## Reuse

- `IntersectionObserver` pattern is already in use by `chat-view-skeleton`
  (`src/dotnet/UI.Blazor/Components/Skeleton/chat-view-skeleton.lit.ts:23`)
  and `active-recording-svg` (post-R2). If R3's option A needs a
  visibility hook, reuse this pattern via `fastRaf` from
  `src/nodejs/src/fast-raf.ts`.
- `content-visibility: auto` was introduced in R1; reuse on any
  always-mounted-but-mostly-off-screen subtree if R5 surfaces more
  candidates.
- `ComputedRenderStateComponent.ShouldRender` field-equality skipping
  pattern in R7 mirrors the existing `_renderedModel == m` check — no
  new mechanism needed.
