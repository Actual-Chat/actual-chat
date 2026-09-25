# Raise hand and reactions in video mode (#4603)

> Status: implemented, uncommitted, not yet tried in a browser (2026-09-24).
> Branch `feat/4603-raise-hand-in-video-mode`.

## As built (differences from the plan below)

- Reactions live on `LiveSessionUI` (`ListReactions`, `SendReaction`), not a new
  `CallReactionsUI` — CODING_STYLE rule 14 (extend an existing UI service).
- `LiveSessionUI.ListRaisedHandAuthorIds` (consolidated) feeds the tiles, so `Get`
  churn doesn't re-render the panel. `GetMutedRecordingChat` now reuses `GetOwnMember`.
- Emoji set: 👍 ❤️ 😂 😲 🥳 🔥 (`CallReaction.AllowedEmojis`) — the ones with animated
  SVGs; 👏 and 🎉 have none. A reaction lives 5 s (`Constants.Call.ReactionDuration`).
- The hand shows as its own badge (`.video-hand-badge`, top-left) plus a `hand-raised`
  ring, not inside `.video-participant-label`, which hides on focused/pip/narrow tiles.
  No per-tile emoji badge: the overlay carries the sender's name.
- The panel footer only renders while expanded, so the ⋮ panel menu also has
  Raise / Lower hand. `CallReactionsOverlay` is in `FullScreenCallView` too.
- Toast key is `Call_HandLowered` ("Your hand was lowered") — a Moderator can lower
  it too, not just the host. No `Call_React` key (toolbar buttons carry no labels).
- **No `IsWatching`** (dropped after review): opening the video panel also starts audio
  listening (`ChatVideoUI.SetWatching`), so a viewer reports `AudioListen` and is already
  present via `IsListening`. A `VideoView`-only record means audio is off - that viewer
  stays `Exited` and can't raise a hand. The plan text below still mentions it.
- Call tab: hands sort first per group.
- Menus from the full-screen call toolbar render *under* the call screen on this
  base; dev's 7b5d0251a7 fixes that (rebase conflicts in `ComputeState`'s `m with`).


## Goal

In a video call, a participant can raise a hand to ask for the floor without
interrupting, and can send a short emoji reaction (👍 ❤️ 😂 👏 🎉) without
speaking.

- One **React** button in the call's bottom toolbar, next to the settings
  (options) button, opens both: raise / lower hand, and the emoji row.
- A **raised hand** stays up until it is lowered, and shows on the raiser's
  **video tile**. The call host (or a chat Owner or Moderator) can lower
  someone else's hand. Leaving the call lowers it.
- An **emoji reaction** is transient: it animates over the video panel for a
  few seconds and disappears on its own.

The hand is **part of the `LiveSession`**: it lives on the session member, and
there is no hand (and no React button) while `ILiveSessions.Get` returns null.

**Deferred on purpose:**
- Showing a raised hand for a participant with no video tile (camera off). The
  data supports it — only the presentation is open.
- The one-camera-plus-viewers call, which has no `LiveSession` today (see
  *Current behavior*), and so no hand.

See *Out of scope*.

## Current behavior

- **The call is already modeled per person on the server.** `LiveSessionsBackend`
  keeps one `ParticipationInfo(Kind, RegisteredAt, MicMuted, JoinedAt)` per
  author in the Redis hash `live-session:participants`. `ILiveSessions.Get`
  merges it with the live audio and video stream lists into a `LiveSession`
  with `Host`, `Rules` and `Members` (`LiveSessionMember`: `IsMicOpen`,
  `HasCamera`, `MicMuted`, `JoinedAt`, `Group`).
- **Video viewers already heartbeat into it.** `LiveSessionUI.RunParticipationSync`
  sends `SetParticipation` for `Record` / `AudioListen` (from `ActiveChatsUI`)
  and `VideoView` (from `ChatVideoUI.GetWatchingChatId`), on every change and
  every 45 s, resending on reconnect. Leaving removes the record;
  `PeerParticipations` removes it for a client that died;
  `ParticipantStaleness` (90 s) is the backstop.
- **`Get` is gated on a latched session, the participants hash is not.**
  `Get` returns null unless the session is a call or `SessionStartedAt` is set,
  and that needs **two** distinct streamers. A group video call with one
  camera and N viewers therefore has participation records but **no**
  `LiveSession`. This plan keeps that gate: no session, no hand.
- **Viewers are `Exited` in `Get`.** `Group` is `Other` only for
  `IsMicOpen || HasCamera || HasScreenShare || IsListening`; a fresh
  `VideoView` record counts for none of them. So in a latched session a viewer
  with mic and camera off — exactly the person who would raise a hand — is
  listed as `Exited`.
- **Moderation exists, and a hand works the same way.** `MutePeer` / `MuteAll`
  set `ParticipationInfo.MicMuted`; `LiveSessions` gates them through
  `CallAuthority` (own state always allowed; someone else's needs
  `RequireManage()` — host, Owner or Moderator — plus Owner immunity, and never
  in a peer chat). `LiveSessionUI.RunMuteEnforcement` reacts to your own
  `MicMuted` with a toast.
- **Typing is the model for anything transient.** `ChatTypingActivitiesBackend`
  is a per-chat author list on the LiveBackend shard, RAM-only (the value lives
  in a `ListRaw` computed primed through `LockingComputeMethodPrimer`), expired
  by `ExpireStale`. Emoji reactions need exactly this.
- **UI.**
  - `VideoPanel` draws a tile per camera stream (`RemoteStreamPlayer` →
    `VideoTrackPlayer`) plus the own preview (`VideoStreamingPreview`), and
    marks speakers with a `speaking` class. Its bottom toolbar is
    `.video-panel-footer`.
  - `FullScreenCallView` has a `.c-toolbar` whose last button is the options /
    settings one (`icon-options-2` → `OnOptionsClick`).
  - `VisualActivityPanel` hosts the call (`VideoPanel`) and the map.
  - Animated emoji already exist: `EmojiIcon` + `Emojis.SvgNames`
    (see `EmojisTestPage`).

Nothing carries "this author wants the floor" or "this author just reacted".

## Design

Two different lifetimes, so two mechanisms:

| | Raised hand | Emoji reaction |
|---|---|---|
| Lives | until lowered / until leaving | ~8 s |
| Stored | `ParticipationInfo` in Redis, read through `LiveSession` | RAM on the LiveBackend shard |
| Who can clear it | the raiser, the host, Owner, Moderator | nobody; it expires |
| Survives shard handover | yes | no (harmless) |
| Ordered | by raise time (the queue) | by send time |

### 1. Raised hand — on the session member

- `ParticipationInfo` gets
  `[property: DataMember(Order = 4), Key(4)] Moment? HandRaisedAt = null`.
  That is the storage; `LiveSession` is the only read path.
- **Every place that rebuilds the record must carry the hand over**, as they
  already carry `MicMuted`: `SetParticipation(isActive: true)` (the heartbeat
  and kind change, line ~373) and `EnsureParticipant` (line ~1087). Today
  `EnsureParticipant` also drops `JoinedAt`; fix both by building from
  `existing with { ... }` instead of the positional constructor.
- **`LiveSessionMember`** gets `[DataMember(Order = 8), Key(8)] Moment? HandRaisedAt`,
  `[DataMember(Order = 9), Key(9)] bool IsWatching`, and
  `[IgnoreMember] bool IsHandRaised => HandRaisedAt is not null`.
- **`Get`:**
  - sets `IsWatching` for a fresh `VideoView` record and counts it for
    `MemberGroup.Other`, so a viewer is a member, not `Exited`;
  - copies `HandRaisedAt` for members that are not `Exited` (a stale record
    must not show a hand).
  - The hand therefore reaches every client with the `LiveSession` it already
    watches — no new read method.
- **Writing:**
  - `ILiveSessionsBackend.SetHandRaised(ChatId, AuthorId, bool isRaised, CancellationToken)`,
    under `_changeLocks.Lock(chatId)` (the lock `SetParticipation` holds, so a
    concurrent heartbeat's read-modify-write cannot resurrect or drop the hand).
    No-op when there is no participation record, or when raising and the
    session is not latched (`SessionStartedAt is null && !IsCall` — the same
    gate as `Get`); lowering always goes through. Raising keeps an existing
    `HandRaisedAt`, so a double-raise is idempotent and the queue order holds.
    It returns early when nothing changes, then `InvalidateGet`.
  - `ILiveSessionsBackend.LowerAllHands(ChatId, CancellationToken)`, mirroring `MuteAll`.
  - `ILiveSessions.SetHandRaised(Session, ChatId, AuthorId targetAuthorId, bool isRaised, CancellationToken)`
    mirrors `MutePeer`: own hand is always allowed; someone else's needs
    `RequireNotPeerChat` + `RequireManage()` and accepts only `isRaised: false`
    (nobody raises a hand for someone else — `StandardError.Constraint`). No
    Owner immunity: lowering a hand silences nobody.
  - `ILiveSessions.LowerAllHands(Session, ChatId, CancellationToken)`:
    `RequireNotPeerChat` + `RequireManage()`.

Lifetime comes free from the existing record:

| Event | The hand |
|---|---|
| Heartbeat, kind change (listen → record → video view) | kept |
| Leave (`SetParticipation(false)`: hang-up, panel close) | record removed → gone |
| App killed / connection lost | `PeerParticipations` removes the record → gone |
| Rejoin | fresh record → down |
| Shard handover | kept (Redis, not RAM) |
| Session closes (last streamer leaves) | the participants hash goes → gone |
| Session drops back to "not latched" | cannot happen: `SessionStartedAt` never resets |

No client lease and no renewal loop.

### 2. Emoji reactions — a transient per-chat list

- `CallReaction` (`Api/Live/`, DataContract + MessagePack):
  `(AuthorId AuthorId, string EmojiId, Moment SentAt)`. `EmojiId` is an
  `Emoji.Id` from `Emojis`. No kind field: the hand is not in this list, and a
  future non-emoji effect can add one.
- `CallReactionEmojis` (`Api/Live/`): the allowed set, `Emojis.ThumbsUp`,
  `Heart`, `Tears`, `Clap`, `Party` (final list with design), in display order.
- `IChatCallReactionsBackend` (`Streaming.Contracts`), LiveBackend shard, a
  copy of `ChatTypingActivitiesBackend` in shape:
  - `[ComputeMethod] Task<ApiArray<CallReaction>> List(ChatId, CancellationToken)`
  - `Task Send(ChatId, AuthorId, string emojiId, CancellationToken)`: one slot
    per author, so a new emoji replaces that author's previous one with a fresh
    `SentAt` (which is also the client's "animate again" signal). `ExpireStale`
    drops it after `Constants.Video.CallReactionDuration` (8 s).
- `IChatCallReactions` (`Api.Contracts/Streaming/`): `List` (consolidated, with
  `ApiArrayComparer<CallReaction>`, `RemoteComputedCacheMode.NoCache`) and
  `Send`. `ChatCallReactions` checks chat membership via `Authors.GetOwn`,
  rejects an emoji outside `CallReactionEmojis`, and drops sends from the same
  author faster than `Constants.Video.CallReactionMinInterval` (0.5 s), so spam
  cannot churn every viewer's list.

The hand deliberately does **not** live here: it must survive a handover, it is
moderated, and it must disappear when its owner leaves.

### 3. Client

**`LiveSessionUI`** (the `ILiveSessions` facade, already home to `Get`,
`MutePeer`, `MuteAll`, `SetHost`) gets:

- `SetHandRaised(chatId, targetAuthorId, isRaised, ct)`, `LowerAllHands(chatId, ct)`.
- `[ComputeMethod] Task<bool> IsOwnHandRaised(ChatId, ct)` — my member in
  `Get`, with the pending optimistic state (below) on top.
- `[ComputeMethod] Task<bool> CanReact(ChatId, ct)` — `Get` is not null, it is
  not a peer chat, and my member is there and not `Exited`.
- Optimistic own state: `ToggleOwnHand(chatId)` sets a
  `MutableState<(ChatId, bool)?> _pendingOwnHand` before the RPC, cleared once
  `Get` agrees or the call fails. The button flips instantly. Other people's
  hands come from `Get` as is.
- `RunHandLoweredNotice`, a chain next to `RunMuteEnforcement`: when my
  member's hand goes from raised to lowered with no local lower pending, show a
  toast ("The host lowered your hand").

**`CallReactionsUI`** (new, `UI.Blazor.App/Services/`, `UIServiceBase<AppUIHub>`):
`[ComputeMethod] List(ChatId)` (empty while `ConnectivityUI.IsConnected` is
false, as `TypingUI` does) and `Send(chatId, emojiId)`.

`ChatVideoUI` needs no change: membership, heartbeats and leave-on-close are
already `RunParticipationSync`.

### 4. UI

1. **The React button.** One `HeaderButton` with `icon-add-reaction`, shown
   when `CanReact` — so only while a `LiveSession` exists, for emoji too —
   in both bottom toolbars:
   - `VideoPanel` footer (`.video-panel-footer`), and
   - `FullScreenCallView` `.c-toolbar`, right before the options button.

   It opens **`CallReactionsPopover`** (new, `Components/VideoPanel/`), which
   is one row of `EmojiIcon` buttons plus a "Raise hand" / "Lower hand" item
   with `icon-hand`, marked `on` while raised. An emoji tap sends and closes;
   the hand item toggles and closes. It uses the existing menu/popover host, as
   `VideoPanelMenu` does.
2. **Raised hand on a tile.** `VideoPanel.Model` gets
   `RaisedHandAuthorIds: AuthorId[]`, taken from `LiveSessionUI.Get` members
   with `IsHandRaised` (plus the own optimistic state). The tile loop adds ` hand-raised` next to
   ` speaking`, for remote tiles and the own preview. `VideoTrackPlayer` takes
   `IsHandRaised` (passed through `RemoteStreamPlayer`) and renders a hand icon
   at the start of `.video-participant-label`. `VideoStreamingPreview` takes the
   same parameter.
3. **Emoji animation.** **`CallReactionsOverlay`** (new,
   `Components/VideoPanel/`) is an absolutely positioned layer inside
   `video-panel-content`, above the tiles and below the toolbars. For every
   `(AuthorId, SentAt)` it has not played yet, it spawns one animated
   `EmojiIcon` that floats up and fades (CSS keyframes, ~2.5 s, then removed;
   no JS timer per emoji — `UITimer` sweeps the played set). The author's name
   rides with the emoji, since a reaction from a tile-less participant must
   still be attributable. Its own `ComputedStateComponent` keeps a reaction
   from re-rendering the panel.
   - On a tile, the same reaction also shows as a small badge on
     `.video-participant-label` for its lifetime. This is a CSS-only addition
     to the tile parameters (step 2 already plumbs per-author state in).
4. **Call tab (`LiveSessionMemberList`).** Viewers now appear as members
   (`IsWatching`, with an eye / camera-view icon where the mic icon would be)
   instead of under Exited. Members with a hand sort first, in
   raise order — the natural queue view for a host — with a hand icon on the
   row, a lower-hand button next to `c-mic-btn` under the same visibility rule
   as mute, and a "Lower all hands" manage button next to mute-all when any
   hand is up.
5. **Localization.** `Call_React`, `Call_RaiseHand`, `Call_LowerHand`,
   `Call_HandRaised`, `Call_LowerAllHands`, `Call_HandLoweredByHost` in every
   `Strings.<lang>.json`, with `// fits:` budgets (#4267) for the popover items
   and the manage button.

### Rejected alternatives

- **One generic reaction service for both.** The hand needs Redis durability,
  moderation and removal on leave; an emoji needs none of that and must not
  rewrite a Redis record or push a whole `LiveSession` to every client for 8 s.
- **A `ListRaisedHands` off the participants hash** (previous draft). It works
  before a session latches, but splits call state across two read paths; the
  hand belongs with the rest of the member state on `LiveSession`.
- **Latching the session on the first video viewer, or showing any live video
  as a session.** Both make the one-camera call a `LiveSession`, but change the
  "Voice chat started" banner / conversation block or put a one-member Call tab
  on every solo camera. Not in this issue.
- **A client-renewed lease service for the hand** (earlier drafts). It
  duplicates the participant registry's lifetime, reconnect and handover
  handling, and leaves moderation to be rebuilt.
- **A chat entry / system message.** Persisted and noisy; this is call state.

## Reuse

### Existing abstractions to reuse

- `LiveSessionsBackend`: `_participants`, `ParticipationInfo`, `_changeLocks`,
  `SafeGetParticipant`, `SafeGetHashMap`, `InvalidateGet`, the `Get` member builder.
- `LiveSessions`: `GetCallAuthority` / `CallAuthority.RequireManage`,
  `RequireNotPeerChat`, the `MutePeer` / `MuteAll` shape.
- `ChatTypingActivitiesBackend` as the template for the reactions backend:
  `ShardComputeService`, `ShardOwner.RequireShardOwnership`,
  `LockingComputeMethodPrimer`, `ExpireStale`.
- `LiveSessionUI.RunMuteEnforcement` (toast pattern), `RunParticipationSync`
  (membership), `ConnectivityUI.IsConnected`, `ApiArrayComparer<T>`.
- `EmojiIcon` + `Emojis` (animated SVG), `HeaderButton`, the menu/popover host,
  `VideoPanelActions`, the `speaking` tile-class pattern,
  `LiveSessionMemberList` row buttons, `icon-add-reaction`, `icon-hand`,
  `ToastUI`, `UITimer`.

### Reusability of new components

- `LiveSessionMember.HandRaisedAt` / `IsWatching` are call-wide, not
  video-specific: `FullScreenCallView`, the Call tab and the deferred no-tile
  presentation read the same `LiveSession`.
- `IChatCallReactions` is chat-scoped, so an audio-only call gets reactions for
  free once a UI exists.
- `CallReactionsPopover` / `CallReactionsOverlay` are call UI; they live in
  `Components/VideoPanel/` and are referenced by both toolbars.

## Steps

1. `/track-issue`: link #4603 (already on the branch) and move it to In Progress.
2. `ParticipationInfo.HandRaisedAt`; carry it through `SetParticipation` and
   `EnsureParticipant` (fixing `JoinedAt` there); `LiveSessionMember.HandRaisedAt`
   and `IsWatching` in `Get`, viewers grouped as `Other`.
3. `ILiveSessionsBackend` / `LiveSessionsBackend`: `SetHandRaised`,
   `LowerAllHands`. `ILiveSessions` / `LiveSessions`: the same
   with the authority rules.
4. `CallReaction`, `CallReactionEmojis`, constants; `IChatCallReactionsBackend` /
   `ChatCallReactionsBackend`; `IChatCallReactions` / `ChatCallReactions`;
   register in `ApiContractsModule` + `StreamingServiceModule`, add the
   `AppUIHub` accessors, regenerate AOT sources (`App.AotHelper -g`).
5. Tests (see below).
6. `LiveSessionUI` additions + `CallReactionsUI`.
7. UI: the React button in both toolbars, `CallReactionsPopover`,
   hand badge + class on tiles, `CallReactionsOverlay`, the Call-tab row,
   lower / lower-all; CSS.
8. Strings in all languages + size budgets; l10n tests.
9. `npm run build:Verify`, `dotnet build ActualChat.CI.slnf`, integration tests.
10. Manual pass: two users via `/debug-ui` — raise, see the tile badge, react
    with emoji, host lowers the hand, toast — then iPhone for the narrow
    toolbar and the popover.

## Edge cases

- **One camera, N viewers (no latched session).** No `LiveSession`, so no
  React button, and the backend ignores a raise. Deferred (see *Out of scope*).
- **The session latches while the panel is open** (a second person opens a
  mic or camera). `Get` turns non-null and the React button appears.
- **Raising right after joining.** The button needs `CanReact` (my member in
  the session), and the backend no-ops without a participation record.
- **Same author on two devices.** One record per author: raising or lowering on
  either is seen by both, which is correct. If one device leaves and the other
  stays with the same participation kind, the kind-guarded removal can take the
  record — and the hand — with it. `MicMuted` has the same flaw today; out of
  scope.
- **Host lowers while the raiser toggles.** Last write wins under `_changeLocks`;
  the optimistic flag clears on the next server list.
- **Emoji from someone with no tile.** The overlay shows the name with the
  emoji, so it is attributable even without a tile.
- **Emoji spam / a flood in a large call.** One slot per author plus the 0.5 s
  floor bounds the list by call size; the overlay caps concurrent animations
  (drop the oldest beyond ~8).
- **Reconnect.** Hands come back with the `LiveSession`. Emoji in flight
  are lost, which is what "transient" means.
- **Peer chats.** No React button (`CanReact` is false), and the server rejects
  lowering the other side's hand.
- **Session close** (last streamer leaves): the participants hash goes with the
  session, so hands go with it.
- **Old clients** ignore the new keys and never call the new methods. No
  version gate.

## Tests

- **Integration, hands** (`Chat.IntegrationTests`, next to the mute tests):
  raise → my `Get` member has `HandRaisedAt`; lower → gone; a second raise keeps the
  first `HandRaisedAt`; the hand survives a heartbeat, a kind change and
  `EnsureParticipant` (via `MutePeer`); leave → gone, rejoin → down;
  **with one streamer and one viewer (`Get` is null) a raise is ignored**;
  a viewer in a latched session is `IsWatching`, grouped `Other`, and can raise;
  host / Owner / Moderator can lower another's hand, a plain member gets
  `Constraint`, nobody can raise another's, peer chat → `Constraint`;
  `LowerAllHands` clears everyone; no participation record → no-op; an
  `Exited` member reports no hand in `Get`.
- **Integration, reactions** (`Streaming.IntegrationTests`, next to
  `ChatTypingActivitiesTest`): send → listed; it expires on its own; a second
  emoji replaces the first and refreshes `SentAt`; an emoji outside the
  allow-list is rejected; sends under the interval are dropped; a non-member's
  send does nothing.
- **Unit:** `LiveSessionUI` optimistic merge (pending raise before the server
  sees it; cleared on the server echo and on RPC failure).
- **E2E** (TS suite, `AC_E2E_SERVER=external`), optional: A raises, B sees the
  tile badge; A reacts, B sees the overlay; B (host) lowers A's hand.

## Open questions

1. **Which emoji, and the popover's look** — the row content and order need a
   design. Default: 👍 ❤️ 😂 👏 🎉, the hand item first.
2. **Does the React button replace anything on a narrow toolbar?** The narrow
   footer is already full (camera, recorder, camera switch). Default: add it as
   one more button and let the row scroll / compress. Is there a Figma design?
3. **Sound or toast when a hand goes up?** Default: no; the tile badge and the
   Call-tab order are the cue.
4. **Auto-lower when the raiser starts speaking** (Google Meet does this)?
   Default: no — VAD false positives would silently lower hands.

## Out of scope (next steps, not this PR)

- **Hands in the one-camera-plus-viewers call.** Needs a `LiveSession` there:
  either latch on the first viewer (changes the banner and the conversation
  block) or let any live video count as a session (a one-member Call tab on a
  solo camera).
- **Showing a raised hand for a participant with no video tile.** The data is
  there (`LiveSession.Members`); only the presentation is open. The options are a strip at the top-left of the panel, an avatar row, or
  a count that opens the Call tab. To be decided after this ships.
- Reactions in audio-only calls (`FullScreenCallView` already gets the React
  button; the overlay is video-panel only for now).
- Persisting hands or reactions in chat history.
- A speaking queue with "give the floor" (auto-unmute on grant).
- Fixing one-record-per-author for multi-device participation.
