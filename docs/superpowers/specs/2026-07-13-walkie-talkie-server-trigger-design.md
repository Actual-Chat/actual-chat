# Walkie-Talkie: Server-Side Speech-Start Push Trigger (Sub-Project A)

Date: 2026-07-13
Status: Approved design, pre-implementation

## Background

Voxt is adding a walkie-talkie mode: a user keeps up to 3 chats in
"continuous listening" state (the existing `ListeningMode.Forever` /
"Keep listening" option) and must reliably *hear* incoming voice
messages in near-real-time, even when the app is backgrounded or killed
by the OS.

The overall feature decomposes into four sub-projects:

- **A. Server: speech-start push trigger + config** — this spec.
- **B. Android: armed/hot lifecycle** — FCM-wake → foreground service →
  rewind playback; connection dropped when idle.
- **C. iOS: Apple Push to Talk framework integration** — PTT device
  token registration, direct-APNs `pushtotalk` sender, aggregate
  channel model.
- **D. Heard receipts** — per-message played-live/played-later/not-heard
  tracking (likely a new `ChatPositionKind`).

A unblocks B and C. The chosen delivery model is *push-wake*: no
persistent idle connections; a high-priority push wakes the recipient's
app when someone starts speaking, and the loss-preserving replay
pipeline (`GetReplayStream` with `rewindOffset`) lets the woken client
play the utterance from its first word despite the 1–3 s wake latency.

## Goals

- Fire a server-side event at the earliest moment a user starts
  streaming speech into a chat.
- Resolve which chat members are "armed" (walkie-talkie mode on) but
  not currently listening, and send each a high-priority data-only FCM
  push (Android devices only in A).
- Leave a clean per-`DeviceType` seam so sub-project C can add the APNs
  `pushtotalk` sender without touching trigger or resolution logic.
- Server-side kill switch and tunables.

## Non-Goals

- Any client behavior on receiving the push (B/C).
- iOS PTT token registration or APNs-direct sending (C).
- Heard receipts (D).
- UX rework of the "Keep listening" option placement.
- New persistent server state; A persists nothing.

## Key Decisions (with rationale)

1. **Recipients are resolved on demand from existing settings** — no
   registry, no dual-write. A user is *armed* for chat X iff
   `UserListeningSettings.AlwaysListenedChatIds` contains X **or**
   `ChatUserSettings(X).ListeningMode == ListeningMode.Forever`. Both
   are per-user KVAS blobs already readable server-side via
   `ServerKvasBackend.ForUser(userId)`; they remain the single source
   of truth. There is no chat→users reverse index, so resolution reads
   each member's settings (Fusion-cached); a member cap bounds the
   fan-out. If this ever becomes a bottleneck, an explicit registry can
   replace the resolution method without changing the trigger.
   (Considered and rejected: a chat-indexed subscription registry —
   dual-write drift risk and migration cost on day one; long-lived
   "armed" LiveSession participants — LiveSession is Redis with
   minutes-scale TTLs and heartbeat-driven liveness, wrong for users
   who are disconnected by design.)

2. **Trigger point is `LiveSessionsBackend.OnStreamRegistered`** — the
   earliest server hook with ChatId + AuthorId, fired from
   `AudioStreamingBackend.ProcessAudio` before any transcript or chat
   entry exists. No streaming `ChatEntry` exists at speech start, so
   entry-based events are unusable for this. The emission is gated to
   voice-carrying streams (`hasVoice`) — video/screen-share registrations
   do not fire it — and the enqueue is try/catch-insulated so a queue
   failure can never abort stream registration.

3. **Decoupled via a domain event** (`SpeechStartedEvent` over NATS),
   not a direct call: Streaming.Service stays ignorant of
   notifications; a broken handler can never stall the audio pipeline.

4. **No `Notify`/`Push` command hop.** Existing message notifications
   coalesce and persist `Notification` rows; walkie-talkie wakes are
   ephemeral, so the event handler resolves and sends directly.

5. **Wake-pending TTL instead of naive per-utterance pushing.**
   `OnStreamRegistered` fires per utterance. The primary dedup is
   participant exclusion (a woken client joins the live session and
   heartbeats, so it stops receiving), but that leaves (a) the 1–3 s
   wake-latency gap, (b) devices that woke but never started listening,
   and (c) FCM adaptive throttling, which demotes high-priority pushes
   that don't produce engagement — spamming wakes would erode the very
   wake guarantee this feature exists for. A per-(user, chat)
   wake-pending entry with a short TTL suppresses re-sends.

   **Invariant (binding on sub-project B):**
   `WalkieTalkieWakeTtl` **<** the client's post-wake keep-listening
   window. Otherwise a recipient who played a message and re-armed
   could have their next wake suppressed. B's post-wake listening
   window must be ≥ 60 s (cf. `Constants.Audio.ListeningDuration`);
   the TTL default is 30 s.

## Architecture & Data Flow

```
Speaker's client
  └─ ILiveAudioStreams.PushStream                      (existing)
       └─ AudioStreamingBackend.ProcessAudio           (existing)
            └─ LiveSessionsBackend.OnStreamRegistered  (existing command)
                 └─ + Enqueue(SpeechStartedEvent)       NEW — ChatId, AuthorId, StartedAt; voice-only, failure-insulated
                        │  (NATS, sharded by ChatId)
                        ▼
NotificationsBackend.OnSpeechStartedEvent               NEW [EventHandler]
  1. Feature gate: Features_EnableWalkieTalkiePush off → return
  2. Member cap: AuthorsBackend.ListUserIds(chatId);
     count > WalkieTalkieMaxChatMembers → return
  3. Armed filter per user (Fusion-cached KVAS reads):
     AlwaysListenedChatIds ∋ chatId
     OR ChatUserSettings(chatId).ListeningMode == Forever
  4. Exclusions: the speaker; active live-session participants
     (GetActiveParticipantUserIds); wake-pending (user, chat) entries
  5. Per recipient: ListDevices → dispatch by DeviceType:
     - AndroidApp → data-only FCM, Priority.High,
       payload { kind: SpeechStarted, chatId, authorId, startedAt },
       TimeToLive ≈ 60 s, latest-wins collapse key per chat
     - iOSApp → skipped in A (seam for C's APNs pushtotalk sender)
     - WebBrowser / WindowsApp → skipped
  6. Record wake-pending entries (in-memory, TTL = WalkieTalkieWakeTtl)
```

The event is sharded by `ChatId`, so all events for a chat land on the
same shard and the wake-pending map can be a plain in-memory TTL'd map.
Worst case after a shard restart is one extra push.

## Components

1. **`SpeechStartedEvent`** — `src/dotnet/Backend/Events/`, alongside
   `ChatEntryChangedEvent`. An `EventCommand, IHasShardKey<ChatId>`
   carrying `ChatId`, `AuthorId`, `StartedAt`. AuthorId only, no
   UserId — chat-scoped contract; the handler resolves users from
   authors.
2. **Emission** — one
   `context.Operation.AddEvent(new SpeechStartedEvent(...))` in
   `LiveSessionsBackend.OnStreamRegistered`.
3. **`Features_EnableWalkieTalkiePush`** — `FeatureDef<bool>`,
   `IServerFeatureDef`, placed next to existing server feature defs.
4. **`OnSpeechStartedEvent`** `[EventHandler]` on `NotificationsBackend`
   plus private `ResolveWalkieTalkieRecipients(chatId, speakerAuthorId)`
   implementing steps 2–4 above. Private by design: it composes
   existing services and has no other plausible consumer.
5. **Push composition** — new data-only message shape in
   `FirebaseMessagingClient`, modeled on the existing `SendDismissal`
   silent push (data-only, no `Notification` payload), adding
   `Priority.High`, short `TimeToLive`, and a per-chat collapse key.
6. **Settings** — on the Notifications.Service settings class:
   - `WalkieTalkieWakeTtl` = 30 s
   - `WalkieTalkieMaxChatMembers` = 100
   `ActiveChatsUI.MaxActiveChatCount = 3` stays a client-side const;
   nothing in A depends on it.

## Reuse

Existing abstractions used (no new equivalents written):

| Need | Existing abstraction |
|---|---|
| Event → handler plumbing | `EventCommand` + `[EventHandler]` pattern (`ChatEntryChangedEvent` → `OnChatEntryChangedEvent` is the template) |
| Chat members | `AuthorsBackend.ListUserIds(chatId)` |
| Armed predicate reads | `ServerKvasBackend.ForUser(userId)` + `.ChatUserSettings(chatId)` / `.UserListeningSettings()` typed accessors (same pattern as the existing `NotificationMode` read in `NotificationsBackend`) |
| Active-listener exclusion | `LiveSessionsBackend` participant tracking via `GetActiveParticipantUserIds` (already used to suppress message notifications for call participants) |
| Device lookup + stale-token pruning | `NotificationsBackend.ListDevices` / existing `Unregistered` handling |
| Wake-pending map | ActualLab `RecentlySeenMap` if it fits; else a trivial timestamp dictionary (verify during planning) |
| Config & gating | `Features` infra + Notifications settings class |

Reusability of new components: `SpeechStartedEvent` is placed in the
shared Backend events project because future consumers are expected
(sub-project D heard receipts, analytics). All other new code is
Notifications-internal; nothing belongs in `ActualChat.Core`.

## Error Handling

- Audio pipeline insulated by construction (event rides NATS).
- Per-recipient isolation: each recipient's KVAS reads and push send
  are individually guarded — one failure logs and skips, never aborts
  the batch.
- NATS event redelivery is idempotent via the wake-pending map.
- FCM token failures reuse the existing pruning path.
- Flag off / oversized chat / zero armed recipients → cheap early
  return; the flag check runs first, so disabled cost per utterance is
  near zero.

## Testing

- **Unit-level (main coverage), against a fake `IFirebaseMessagingClient`:**
  armed-by-`AlwaysListenedChatIds`; armed-by-`ListeningMode.Forever`;
  speaker excluded; active participant excluded; wake-TTL suppression
  and expiry (virtual clock); member-cap skip; flag-off skip;
  one-recipient-fails-others-send.
- **Integration:** `OnStreamRegistered` emits `SpeechStartedEvent` and
  the handler receives it, following existing Notifications
  integration-test patterns.
- **Out of scope:** real device wake behavior (sub-project B's
  end-to-end concern).
