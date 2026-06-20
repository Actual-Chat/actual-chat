# Live-location chat message (persisted) — #3952, Phase-2 chat entry

Companion to [`location-sharing.md`](location-sharing.md). That feature ships live
location as **ephemeral reactive state** (a chat banner + a map modal). This plan adds
the **Telegram/WhatsApp-style persisted message**: when a user shares their location, a
real chat entry appears in the timeline showing a **map thumbnail** that **refreshes on
an interval** while the share is live, and **remains in history** after it ends.

## Context

Today there is no entry in the message list for a share — only the reactive
`LiveLocationBanner` + `LiveLocationMapModal`. Users expect a message bubble with a map
image in the conversation (like Telegram/WhatsApp), that updates as the sharer moves and
stays visible afterwards ("Live location ended"). This was deferred in the parent plan
(open question 5; *"posting a live-location chat entry is deferred to a later phase"*).
The parent plan deliberately avoided this because a mutable chat entry **spams the
edit/invalidation machinery** — that cost is now accepted in exchange for persistence,
and mitigated with a **coarse refresh interval**.

---

## Reuse (mandatory)

### Existing abstractions to reuse

| Concern | Reuse | Path |
|---|---|---|
| Create/update a chat entry | `Chats_UpsertEntry` (`LocalId == null` create, non-null update) → `Chats.OnUpsertEntry` → `ChatsBackend.OnChangeEntry` | `Api.Contracts/Chat/IChats.cs`, `Chat.Service/Chats.cs`, `Chat.Service/ChatsBackend.cs` |
| Image attachment on an entry | `ChatEntryAttachment` (`MediaId` + optional `ThumbnailMediaId`) + `ChatsBackend_CreateAttachments`/`RemoveAttachments` | `Api/Chat/ChatEntryAttachment.cs`, `Chat.Service/ChatsBackend.cs`, `Chat.Service/Db/DbChatEntryAttachment.cs` |
| Image media + storage | `Media` / `MediaKind.Image`, the upload pipeline | `Api/Media/Media.cs`, `IUploads`, `IMedia` |
| Render an image attachment + full viewer | `ChatEntryAttachmentsView`, image-attachment view, `VisualMediaViewerModal` | `UI.Blazor.App/Components/ChatView/...` |
| Extensible inline content (caption/marker) | `Markup` base + `TypeMapper<IMarkupView>` (cf. `MentionMarkup`/`MentionMarkupView`); `SystemEntry.ToMarkup()` for system-ish entries | `Api/Chat/Markup/*`, `UI.Blazor.App/Components/MarkupView.cs` |
| Map rendering + full-screen view | `MapView` + `LiveLocationMapModal` (Phase 2/3) | `UI.Blazor/Components/MapView/*`, `UI.Blazor.App/Components/LiveLocationMapModal/*` |
| Live position + share lifecycle | `ILiveLocations` (List/Get) + `LocationUI` (the reporter that already knows when a share starts/updates/stops) | `Api.Contracts/Chat/ILiveLocations.cs`, `UI.Blazor.App/Services/Location/LocationUI.cs` |
| Timing constants | `Constants.LiveLocation` | `Api/Constants.cs` |

**No static-map raster exists.** OpenFreeMap (our tile source) is **vector-only** — there
is no static-image endpoint. Producing a thumbnail is genuinely new (see Open questions).

### Reusability of new components

- **Static-map thumbnail generator** (turn a `GeoPoint` → PNG) — useful beyond this
  feature. → Place the TS in the **`MapView` module** (`UI.Blazor/Components/MapView/`)
  as an exported helper, not buried in a feature file.
- **`LocationMarkup` + `LocationMarkupView`** (if chosen over a new entry kind) → markup
  in `ActualChat.Api`, view in `UI.Blazor.App` (registered in the `IMarkupView` map),
  mirroring `MentionMarkup`.

---

## Recommended approach

Drive everything from the **existing `LocationUI`** (it already owns the share lifecycle),
so the persisted entry stays in lock-step with the ephemeral share.

1. **Post once on start.** In `LocationUI.StartSharing`, after the first fix, post a single
   chat entry via `Chats_UpsertEntry` (create) — caption "Live location" + an initial map
   thumbnail attachment. Remember its `LocalId` next to the `ActiveShare`.
2. **Thumbnail = client-side MapLibre snapshot** (recommended, no keyed provider): render
   the point in an **offscreen `MapView`** created with `preserveDrawingBuffer: true`, wait
   for the map `idle` event, `canvas.toDataURL('image/png')`, then upload through the
   existing media/upload pipeline to get a `MediaId`. Reuses OpenFreeMap + `IUploads`/`IMedia`;
   AGPL-clean. *(Alternative: a keyed static-map API — see Open questions.)*
3. **Refresh on a coarse interval.** The `LocationUI` loop regenerates the thumbnail every
   `ThumbnailRefreshPeriod` (**new constant, ~60 s** — deliberately coarser than the 10 s
   position `UpdatePeriod`) and **only when the position moved materially**, then calls
   `Chats_UpsertEntry` (update, same `LocalId`) swapping the attachment and refreshing the
   "· Xm left" caption. Coarse interval + move-threshold keeps entry-edit fan-out bounded.
4. **Finalize on stop/expiry.** Stop refreshing; update the entry once more to caption
   "Live location ended" and freeze the last thumbnail. The message **persists in history**.
5. **Render the card.** Reuse image-attachment rendering for the thumbnail with a small
   overlay ("Live location" + live "· Xm left" / "ended"); the live countdown/updated-ago
   reads the reactive `ILiveLocations.Get(chatId, authorId)`. **Tap → existing
   `LiveLocationMapModal`.**
6. **Identify the entry** as a live-location card via a lightweight marker — a
   `LocationMarkup` caption (preferred; no schema change) **or** a new
   `ChatEntryKind.LiveLocation`. Recommend `LocationMarkup` + `LocationMarkupView` to avoid
   a `ChatEntryKind` enum value + EF migration.

### Files (anticipated)

- `Api/Chat/Markup/LocationMarkup.cs` (new) + register render in `IMarkupView` map
  (`UI.Blazor.App/Module/BlazorUIAppModule.cs`) + `LocationMarkupView.razor` (new).
- `UI.Blazor/Components/MapView/map-view.ts` (+ `MapView.razor.cs`) — add a `snapshot()`
  helper; `MapView` map options gain `preserveDrawingBuffer`.
- `UI.Blazor.App/Services/Location/LocationUI.cs` — post/refresh/finalize the entry; hold
  the entry `LocalId` in `ActiveShare`.
- `Api/Constants.cs` — add `LiveLocation.ThumbnailRefreshPeriod` + a move threshold.
- Reuse `Chats_UpsertEntry` / media upload — **no new backend command expected.**

---

## Phases (each ends green/buildable)

1. **Thumbnail generator** — `MapView.snapshot()` (offscreen render → PNG) + upload →
   `MediaId`. *Gate:* given a `GeoPoint`, produces a viewable image media.
2. **Post + finalize** — `LocationUI` posts the entry on start, finalizes on stop/expiry.
   *Gate (integration):* starting a share creates one entry; stopping finalizes it; it
   survives in history.
3. **Periodic refresh** — coarse-interval thumbnail/caption updates with move-threshold.
   *Gate:* moving the sharer swaps the thumbnail at most once per `ThumbnailRefreshPeriod`.
4. **Card rendering** — `LocationMarkupView` (thumbnail + overlay + tap→modal), live
   countdown from reactive state. *Gate (web/CDP):* a map-card message appears, updates,
   and opens the full map.
5. **Polish/QA** — "ended" state, own-vs-others styling, reduced-motion, battery/cost
   sanity on the refresh cadence.

---

## Verification

- **Backend (integration):** extend `tests/Chat.IntegrationTests/LiveLocationsTest.cs` (or
  a sibling) — sharing creates exactly one location entry; update swaps its attachment;
  stop/expiry finalizes; the entry remains readable (persisted) afterwards.
- **e2e (CDP, web):** extend `tests/ts/e2e/location-sharing.test.ts` — after starting a
  share, assert a **location message with an `<img>` thumbnail** appears in the chat
  timeline; move the mock GPS and assert the thumbnail/caption refresh; **Stop** and assert
  the message **stays** in history with an "ended" caption. Run via
  `AC_E2E_BROWSER=cdp AC_E2E_SERVER=external HostSettings__BaseUri=http://localhost:7180`.

---

## Open questions

1. **Static-map image source.** *Recommend* client-side MapLibre snapshot (no key, reuses
   OpenFreeMap + media pipeline). *Alternative:* a keyed static-map API (e.g. MapTiler
   Static Maps) — simpler rendering, but adds a proprietary, billed dependency + key
   management; weigh against the AGPL/no-proprietary stance in the parent plan.
2. **Refresh interval + move threshold.** Proposed ~60 s and "only on material movement".
   Tighter = smoother but more entry edits/fan-out and more thumbnail uploads.
3. **Entry identity.** `LocationMarkup` caption (no migration) vs new
   `ChatEntryKind.LiveLocation` (cleaner typing, needs enum + migration). Recommend the
   former.
4. **One entry per share** (reused/finalized) — confirm we never post a fresh entry per
   position update (that would be the spam we're avoiding).
5. **Initiation scope.** Sharing now works on web too (this session). Should the persisted
   message be posted regardless of platform? Default **yes** (symmetry with the banner).
6. **History cost.** Many old location cards = many thumbnail images stored; confirm
   retention is acceptable (images are normal `Media`, subject to existing cleanup).
