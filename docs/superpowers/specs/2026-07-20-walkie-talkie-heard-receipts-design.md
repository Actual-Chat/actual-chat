# Walkie-Talkie: Heard Receipts (Sub-Project D)

Date: 2026-07-20
Status: Implemented

## Background

Sub-projects A (server speech-start push trigger), B (Android headless
wake), and C (iOS Apple Push to Talk) are complete: a walkie-talkie
recipient now hears incoming voice messages headlessly — screen off,
app backgrounded or killed — via push-wake and replay playback. That
success creates the sender's problem D solves: the recipient *heard*
the message but never opened the chat, so their Read position never
moves and the sender keeps seeing the message as unread. The sender
has no way to tell "delivered into the void" from "heard".

D adds the missing feedback signal: when a walkie-talkie listener's
device actually starts playing a voice message, the sender's existing
transient "Unread" label clears — exactly as if the recipient had read
it.

## Goals

- Record, per user, a *heard* watermark for continuously-listened
  (walkie-talkie) chats, advanced when playback of a voice message
  starts on the listener's device — including headless push-wake
  replay playback.
- Surface it to the sender through the **existing** unread machinery:
  the transient "Unread" label on own messages clears when another
  member has read *or* heard past that entry. Zero new visual
  elements.
- Keep heard fully separate from Read: the listener's Read position,
  unread counters, and notification behavior are untouched.

## Non-Goals

- Any new UI: no per-message status rows, ticks, avatars, or
  "played live / played later / not heard" distinctions (Voxer-style
  persistent status was considered and rejected as visual noise).
- Advancing the Read position from playback ("playback = read") —
  explicitly deferred; v1 keeps the kinds independent.
- Heard tracking outside walkie-talkie chats. A recipient playing a
  voice message in a normal chat has the chat open on screen, so the
  ordinary read position covers the sender's label anyway.
- Per-message, per-recipient receipt storage (reactions-style tables)
  — rejected in favor of the watermark model.
- Playback-progress thresholds: *playback started* counts as heard.

## Key Decisions (with rationale)

1. **Heard is a new `ChatPositionKind` on the existing read-position
   machinery** — `ChatPositionKind.Heard` alongside `Read` and `View`.
   Per-user truth lives in the Users DB `ChatPositions` table
   (`DbChatPosition`, key `"{userId} {chatId}:{kind}"`), sharded by
   user like every other position — no new tables, no new services.
   Forward-only, same as Read. (Considered and rejected: per-entry
   receipt tables in the Chat DB modeled on Reactions — user directed
   the read-positions approach precisely because per-user state
   belongs in the user-sharded store.)

2. **Watermark semantics** — heard lid N means "everything ≤ N is
   consumed" for sender-side labels, including interleaved text
   entries. This matches the read-position philosophy (scrolling to
   the bottom marks all prior entries read) and is accepted as v1
   behavior.

3. **Sender visibility rides the existing `ReadPositionsStat` bridge.**
   `ChatPositionsBackend.OnSet` for kind `Heard` additionally enqueues
   the same `ChatsBackend_UpdateReadPositionsStat` command it enqueues
   for Read. The stat keeps the max lid per user, so a user's stat
   entry becomes `max(read, heard)`, and the sender-side label clears
   through the *unchanged* `ReadPositionsStat.HasReadByAnotherAuthor`
   path (`ChatEntryMessageView` "Unread" label, `ChatListItem`,
   `NotifyAllButton`). No UI code changes.

4. **The heard write path is a server-side ack keyed by stream, not a
   client-side position write.** The client never calls
   `ChatPositions_Set` for heard. Instead it reports playback to the
   streaming service — modeled on the existing
   `ILiveAudioStreams.ReportAudioLatency(Session, latency, ct)`
   precedent already called from `ChatListeningPlayer`:

   `ReportPlayback(Session session, ChatId chatId, string streamId,
   ChatEntryId entryId = default, CancellationToken ct)`

   The server resolves the entry and issues the backend position set
   (`IChatPositionsBackend.OnSet`, kind `Heard`, forward-only) with
   the session's UserId. Rationale: the stream→entry mapping lives
   server-side; the client stays dumb and cannot spoof arbitrary lids
   beyond streams it actually received.

5. **The two playback paths differ in what the client knows:**
   - **Live listening** (`ChatListeningPlayer`): `LiveAudioStreamInfo.
     EntryId` is null — the text entry is created mid-stream during
     transcription and linked via `ChatEntry.ContentStreamId` /
     `ChatEntryAudio.StreamId`. The ack carries only `streamId`; the
     server resolves streamId → entry. At ack time (playback start,
     seconds into the stream) the entry almost always exists; a short
     server-side retry covers the creation race.
   - **Replay** (`ChatReplayPlayer` / headless walkie-talkie wake via
     `GetReplayStream`): `ReplayStreamMuxer.ProcessEntry` populates
     `EntryId = entry.Id` (and `StreamId = audio.StreamId ??
     blobId`). The ack carries `entryId` directly; the server skips
     resolution. This path is mandatory — the primary D scenario
     (push-woken headless playback) is replay, not live.

6. **Ack timing = track actually starts rendering**, not enqueue —
   hooked at the playback layer so "heard" stays truthful (a track
   enqueued but never played, e.g. focus loss, records nothing).

7. **Walkie-talkie-only gating, client-first.** The client sends acks
   only for chats in its continuously-listened set
   (`UserListeningSettings.AlwaysListenedChatIds` or
   `ChatUserSettings.ListeningMode == Forever` — the same "armed"
   definition sub-project A resolves server-side). The server may
   cheaply re-check the same KVAS settings as belt-and-suspenders;
   this is an implementation option, not a v1 requirement.

## Data & Control Flow

```
listener device                     server
---------------                     ------
track starts rendering
  └─ chat armed? (AlwaysListened /
     ListeningMode.Forever)
       └─ ReportPlayback(session,
            chatId, streamId,
            entryId?)  ──────────►  resolve entry:
                                      entryId given → use it
                                      else streamId → entry lookup
                                        (ContentStreamId / Audio.StreamId,
                                         short retry on miss)
                                    IChatPositionsBackend.OnSet(
                                      userId, chatId, Heard, lid)
                                      └─ forward-only watermark
                                      └─ enqueue
                                         ChatsBackend_UpdateReadPositionsStat
                                           └─ stat entry = max(read, heard)

sender device
-------------
ReadPositionsStat invalidated → HasReadByAnotherAuthor flips
  → transient "Unread" label clears (existing code, unchanged)
```

## Reuse

Existing abstractions (all reused as-is or minimally extended):

- `ChatPositionKind` / `ChatPosition` / `IChatPositions` /
  `IChatPositionsBackend` / `DbChatPosition` — extended with the
  `Heard` enum member only; storage, sharding, forward-only logic,
  invalidation all inherited.
- `ChatsBackend_UpdateReadPositionsStat` + `DbReadPositionsStat` +
  `ReadPositionsStat.HasReadByAnotherAuthor` — untouched; Heard just
  becomes a second enqueue source.
- `ILiveAudioStreams` (`ReportAudioLatency` precedent) — gains the
  `ReportPlayback` sibling; same service, same client call pattern
  from `ChatListeningPlayer`.
- `LiveAudioStreamInfo.EntryId` / `ReplayStreamMuxer` — already carry
  the replay-side entry linkage; no changes.
- `UserListeningSettings.AlwaysListenedChatIds` /
  `ChatUserSettings.ListeningMode` — existing armed definition, read
  on the client for gating (and optionally server-side, as A does).

New components and placement: `ReportPlayback` belongs on the existing
`ILiveAudioStreams` API (Api project) with its handler in
Streaming.Service — no new shared abstractions are introduced, so no
Core placement question arises. The streamId → entry resolution helper
lands next to the transcription-side code that already knows the
linkage; if a general "find entry by stream id" query is added to
`IChatsBackend`, it is reusable by definition.

## Open Questions (to resolve during planning)

- Whether an entry-by-streamId query already exists in `ChatsBackend`
  or the transcription pipeline, or needs adding (plus which index).
- Exact hook point for "track started rendering" acks shared by both
  players (`Playback.OnTrackPlayingChanged` vs per-player hooks).
- Whether the server-side armed re-check ships in v1.

## Testing

- Unit: `Heard` kind forward-only semantics; `OnSet(Heard)` enqueues
  the stat update; stat entry equals `max(read, heard)` per user;
  `HasReadByAnotherAuthor` true via heard-only watermark.
- Unit: `ReportPlayback` resolution — entryId fast path, streamId
  lookup path, retry-on-missing-entry race, gating rejection (if
  server check ships).
- Integration: sender posts voice message → listener replays it
  headlessly → sender's `ReadPositionsStat` reflects the heard lid
  while the listener's Read position stays put and unread counters
  are unchanged.
- Manual (host): two-device walkie-talkie pass — speak on device A,
  hear headlessly on device B, watch A's "Unread" label clear.
