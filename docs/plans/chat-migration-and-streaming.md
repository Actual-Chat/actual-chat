# Chat migration and external streaming

## Goal

Add the product capabilities needed to build repeatable product-video scenarios and to migrate
real group-chat history into Voxt without weakening authorship or timeline guarantees.

The work is split into five independently reviewable plans:

1. [Object maintenance](./object-maintenance.md) — the implemented Users-backed maintenance
   service. Owner-controlled import sessions build on it.
2. [Chat history reset](./chat-history-reset.md) — an owner-only, flow-driven operation that removes
   a chat's complete visible history and all dependent state.
3. [Import-compatible history export](./chat-history-export.md) — a stable snapshot and loss report
   that tells an owner whether current history can be restored by the first importer.
4. [Consented chat history import](./consented-chat-import.md) — group-chat and Place imports with
   per-member consent, sequential timestamps, server-assigned IDs, imported media, and provenance.
5. [External transcript streaming](./external-transcript-streaming.md) — stream plain text alone or
   audio plus a producer-supplied transcript, while leaving server-transcribed audio unchanged.

## Why these are product features

Browser automation can already create NPC accounts, edit profiles, create Places and chats, drive
signed-in clients, and perform ordinary user actions. Those activities belong in the capture rig.
The missing pieces require server authority that browser automation must never acquire:

- attributing historical entries to another member;
- choosing a historical entry timestamp;
- clearing an entire timeline safely;
- freezing a chat or Place while a long-running mutation is in progress;
- importing media under a consenting member's attribution;
- accepting an external real-time transcript instead of running Voxt transcription.

No capture-only backdoor is planned. The same contracts support actual migrations and integrations.

## Shared decisions

### Supported scopes

- Import and maintenance support group chats and Places.
- A Place operation applies immediately to every chat in that Place, including its root chat where
  applicable. Consent is granted once for the Place, not per child chat.
- Peer chats are out of scope.
- Anonymous authors and anonymous chats are out of scope.

### Maintenance behavior

Starting an import immediately places its entire chat or Place in maintenance mode. Ordinary
posting, editing, removal, reactions, pins, calls, recording, and other entry-producing operations
are blocked for everyone. Owners retain only the maintenance/import commands appropriate to the
operation.

Any owner may end an import session. A non-owner cannot stop the import, but can decline or revoke
consent for messages attributed to that member. A history reset cannot be cancelled once deletion
starts; interruption pauses the flow and maintenance remains active until the flow resumes or an
administrator repairs it.

### Timeline invariant

Voxt assigns every local entry ID. Import callers never choose IDs.

Every accepted imported timestamp must be strictly later than the last available visible entry.
The server reads that entry for every batch, sorts the submitted entries by timestamp, and reports
old or equal timestamps as per-item errors in original input order. Accepted timestamps are unique;
rejected entries do not advance the bound.

Prepare or clear history before starting import. Reset and ordinary removal are unavailable inside
an import session. Export/restore tooling is separate future work; this feature assumes the owner
has already prepared the target timeline.
Removed entries retain their old IDs as tombstones; newly imported entries receive higher IDs and
still form a valid new visible timeline.

The exporter fails closed for a round-trip package when the first importer cannot restore some
state, including descendant threads or unsupported entry kinds. It reports those blockers before
the owner is allowed to rely on reset-and-restore. A general archive may still contain such data,
but it must not be labeled import-compatible.

### Import consent and attribution

Every member, including the initiating owner, explicitly chooses whether messages may be
imported under that member's identity. An owner can import only for a member whose consent is active
in the current session.

Imported content records both identities:

- `AuthorId` — the member to whom the message or media is attributed;
- The import session records its initiating owner; upload staging records the uploader separately.
  Per-entry source provenance is future work.

`IsImported`, `ImportSessionId`, the source namespace, and the stable source item ID are immutable
provenance. Revocation prevents future imports; it does not remove content already imported.

### Imported entry shapes

The first version imports user-authored text entries with:

- text content;
- historical `BeginsAt`;
- an optional reply reference to an earlier or same-batch source message;
- zero or more finalized imported-media references;
- optional audio, which requires transcript text plus a valid text-to-audio time map.

Thread reconstruction, reactions, pins, calls, locations, and arbitrary system entries are not part
of the first importer. They can be recreated through ordinary product APIs after the import ends.
Clearing does remove their existing state.

### External streaming shapes

Three producer paths remain distinct:

- Existing audio streaming with server transcription remains unchanged.
- The `feat/text-entry-streaming` work streams ordinary markup messages, including LLM output.
- The new external-transcript path streams plain transcript text, with optional audio. With audio it
  creates a playable voice entry; without audio it finalizes as a plain text entry.

The external producer supplies text deltas with source audio offsets. The server builds and validates
the final `LinearMap` while the stream is running. It never infers timing from network arrival time.

## Reuse

### Existing abstractions to reuse

- `Chat`, `Place`, `ChatId`, `PlaceId`, `AuthorId`, `ChatEntry`, `ChatEntryDiff`, `ChatEntryAudio`,
  `Transcript`, `TranscriptDiff`, and `LinearMap` from `ActualChat.Api`.
- `IChats`, `IAuthors`, `IPlaces`, `IRoles`, `IUploads`, and their backend counterparts.
- `ChatPermissions` / `PlacePermissions` and resolved `AuthorRules` / `PlaceRules`.
- `Flow`, `FlowHub`, and existing chat indexing/conversation flows for resumable history clearing.
- `IAudioStreamingBackend.PushTranscript`, `ILiveAudioStreams.PushStream`, `AudioFrame`, and the
  stream registry used by native live audio.
- `TextEntryStreamer` and `IChats.StreamEntry` from `feat/text-entry-streaming` after that branch is
  merged or rebased.
- The notification lifecycle and notification action infrastructure for import invitations.

### Reusability of new components

- Maintenance contracts and guards belong in `ActualChat.Api` / `ActualChat.Api.Contracts` and
  `ActualChat.Chat.Service`, because reset, import, future migrations, and repair jobs all need them.
- Import provenance types belong in `ActualChat.Api`; they are useful to exports, moderation, audit,
  and future source-specific import adapters.
- Incremental transcript validation belongs beside `Transcript` in `ActualChat.Api`, not inside an
  import or MCP component, because both import and live external streaming consume it.
- Source-specific converters do not belong in the server. They should emit the documented neutral
  import format from separate tools or integrations.

## Delivery order

1. Object maintenance (implemented).
2. Consented import sessions, batch entries and attributed media.
3. Import-compatible history export and its loss/readiness report (separate work).
4. History reset before an import session (separate work).
5. Merge/rebase ordinary text streaming and add external transcript streaming.
6. Build the capture rig against these public product surfaces.

Import does not depend on implementing automated history reset. External streaming is independent
of import and can proceed in parallel once the ordinary text-streaming branch is reconciled.

## ActualChat-docs deliverables

- An owner guide for maintenance mode, Place-wide impact, consent, reset progress, and recovery.
- A migration guide covering snapshot readiness, export, author consent, media staging, ordered
  batches, source-ID reply mapping, reset-and-prepend, and session completion.
- API reference pages for maintenance, export, reset, import, and external transcript streaming.
- Copy-paste examples for text, attachment, imported voice, transcript-only streaming, and
  audio-plus-transcript streaming, including validation and retry behavior.
