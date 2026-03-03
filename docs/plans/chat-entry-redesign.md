# Chat Entry Refactoring: Eliminate Audio Entries

**Status: IMPLEMENTED** (all 10 steps complete, builds clean)
**Branch**: `feat/chat-entry-refactoring`

## Context

Previously, the chat system had two parallel entry sequences per chat: **Text** (Kind=0) and **Audio** (Kind=1). When a user recorded audio, the backend created both an audio entry (stores blob reference, streaming info, timing) and a text entry (transcript), linking them via `AudioEntryLid`. This duplicated state, complicated the ID system (`ChatEntryId` -> `TextEntryId` / `AudioEntryId`), and required the playback system to scan a separate entry sequence.

**Goal**: Collapse to a single text-only entry stream. Audio data moves to the existing **Media** infrastructure. Each text entry gains a `MediaOrStreamId` field -- either a `MediaId` (for historical audio) or a `StreamId` (for live streaming).

---

## New Design

### ChatEntry changes
- **Added**: `string MediaOrStreamId` -- holds either a `MediaId` value (parseable as `MediaId`) or a stream ID string (for live audio). Empty = no audio.
- **Added**: `ChatEntryAudio? Audio` -- populated on reads from the Media table when `MediaOrStreamId` is a valid `MediaId`. Contains the audio timing/metadata needed for playback.
- **Removed**: `AudioEntryLid`, `VideoEntryLid`, and their MemoryPack wrappers.
- **Removed**: `HasAudioEntry`, `HasVideoEntry`, `HasMediaEntry` computed properties.
- **Added**: `HasAudio` -- `Audio != null || !MediaOrStreamId.IsNullOrEmpty()`.
- **Updated**: `HasMarkup` now uses `HasAudio` instead of the removed `HasMediaEntry`.
- **Kept**: `StreamId` for transcript streaming (separate concept from audio streaming).
- **Kept**: `TimeMap` on ChatEntry (it maps text positions to audio time).

### ChatEntryAudio (read-only view, populated on reads)
New record at `src/dotnet/Api/Chat/ChatEntryAudio.cs`:
```
ChatEntryAudio:
  MediaId            -- resolved from MediaOrStreamId
  ContentId          -- from Media.ContentId (blob path)
  BeginsAt           -- from Media metadata
  EndsAt             -- from Media metadata
  ContentEndsAt      -- from Media metadata
  ClientSideBeginsAt -- from Media metadata
  Duration           -- computed
```
Populated on reads (like `Attachments` and `LinkPreviews`) -- not stored directly.

### Audio in Media table
- **Scope**: Uses `ChatId.Value` as the `MediaId` scope.
- **ContentId**: Points to the audio blob in `BlobScope.AudioRecord` storage.
- **Metadata**: Audio timing (`BeginsAt`, `EndsAt`, `ContentEndsAt`, `ClientSideBeginsAt`) stored as ticks in `Media.Metadata` PropertyBag.
- **ContentType**: `audio/webm`.

### Recording flow (ProcessAudio)
1. No longer creates audio entries.
2. Registers live stream in `LiveBackend` as before.
3. Creates text entry with `MediaOrStreamId = streamId` (live phase).
4. After audio is saved to blob, creates `Media` record in Media table.
5. Finalizes text entry with `MediaOrStreamId = mediaId.Value` (historical phase).

### Playback flow
- **ChatReplayer**: Scans text entries (not audio entries), filters by `entry.HasAudio`. Uses `entry.Audio.BeginsAt/EndsAt` for timing.
- **ChatEntryPlayer**: For streaming: uses `entry.MediaOrStreamId` as stream ID. For historical: resolves `MediaId` -> `Media.ContentId` -> blob download.
- **ChatListener**: Already uses `LiveStreamInfo` directly -- minimal changes.

### Public APIs
- `IChats.GetIdRange()` and `IChats.GetTile()` no longer take `ChatEntryKind` parameter.
- `IChatsBackend.GetIdRange()` and `IChatsBackend.GetTile()` same.
- `ChatEntryReader` no longer takes `ChatEntryKind` in constructor.
- Backend internally hard-codes `ChatEntryKind.Text` for DB queries.

### Audio cleanup
- When a text entry with audio is removed, the associated `Media` record is deleted.
- When audio is stripped from an entry during edit (e.g., major transcript edit), the old `Media` record is deleted.
- Implemented in `ChatsBackend.OnChangeEntry`.

### Data migration
- `ChatsUpgradeBackend_MigrateAudioEntries` command runs on startup via `ChatDbInitializer.RepairData`.
- Migrates old text entries that have `AudioEntryId` set but no `MediaOrStreamId`: looks up the audio entry, creates a `Media` record with the blob reference and timing, sets `MediaOrStreamId`.

---

## Implementation Steps (all complete)

| Step | Description | Status |
|------|-------------|--------|
| 1 | Define `ChatEntryAudio` and add `MediaOrStreamId` to ChatEntry | Done |
| 2 | Add DB column (`MediaOrStreamId`) and dual-write, resolve audio from Media table | Done |
| 3 | Rewrite recording flow (ProcessAudio) -- no more audio entries | Done |
| 4 | Rewrite playback (ChatReplayer + ChatEntryPlayer) | Done |
| 5 | Update LiveBackend live streaming discovery | Done |
| 6 | Update UI components (message views, menus, playable text) | Done |
| 7 | Remove `ChatEntryKind` from public APIs (~45 files) | Done |
| 8 | Remove old fields (`AudioEntryLid`, `VideoEntryLid`, `HasAudioEntry`, etc.) | Done |
| 9 | Data migration (`ChatsUpgradeBackend_MigrateAudioEntries`) | Done |
| 10 | Audio cleanup rule (remove Media on entry delete/edit) | Done |

## Backward Compatibility Notes

- `AudioEntryId` type and `ChatEntryKind.Audio` enum value are marked `[Obsolete]` but kept for DB backward compatibility (parsing old IDs, CopyChat, seed data).
- DB columns `AudioEntryId` and `VideoEntryId` on `DbChatEntry` are kept but no longer read in `ToModel()` or written in `UpdateFrom()`.
- `ChatsBackend.CopyChat` and `ChatsUpgradeBackend.InitializeData` suppress the obsolete warnings since they operate at the DB level with old data.
- The data migration (`Step 9`) runs on startup and handles all old entries.

## Remaining Work

- **Manual testing**: Record audio, play back, edit transcribed messages, delete messages with audio -- verify end-to-end.
- **Integration tests**: Run the full test suite after server is available.
- **Post-migration cleanup** (future): After confirming migration ran successfully on all environments, the `[Obsolete]` types (`AudioEntryId`, `ChatEntryKind.Audio`) and DB columns can be fully removed.

## Key Files Reference

| Area | File |
|------|------|
| ChatEntry model + diff | `src/dotnet/Api/Chat/ChatEntry.cs` |
| ChatEntryAudio | `src/dotnet/Api/Chat/ChatEntryAudio.cs` |
| ChatEntryKind enum | `src/dotnet/Api/Identifiers/ChatEntryKind.cs` |
| AudioEntryId (obsolete) | `src/dotnet/Api/Identifiers/AudioEntryId.cs` |
| DbChatEntry | `src/dotnet/Chat.Service/Db/DbChatEntry.cs` |
| ChatsBackend (tiles, audio map, cleanup) | `src/dotnet/Chat.Service/ChatsBackend.cs` |
| Chats (edit/remove/restore) | `src/dotnet/Chat.Service/Chats.cs` |
| IChats API | `src/dotnet/Api.Contracts/Chat/IChats.cs` |
| IChatsBackend API | `src/dotnet/Chat.Contracts/IChatsBackend.cs` |
| ChatEntryReader | `src/dotnet/Api.Contracts/Chat/ChatEntryReader.cs` |
| ProcessAudio | `src/dotnet/Streaming.Service/Backend/StreamingBackend.ProcessAudio.cs` |
| AudioSegmentSaver | `src/dotnet/Streaming.Service/Services/AudioSegmentSaver.cs` |
| ChatReplayer | `src/dotnet/UI.Blazor.App/Services/Playback/ChatReplayer.cs` |
| ChatEntryPlayer | `src/dotnet/UI.Blazor.App/Services/Playback/ChatEntryPlayer.cs` |
| LiveBackend.ChatState | `src/dotnet/Streaming.Service/Backend/LiveBackend.ChatState.cs` |
| Migration command | `src/dotnet/Chat.Service/ChatsUpgradeBackend.cs` |
| DB migration | `src/dotnet/Chat.Service.Migration/Migrations/20260225232058_AddMediaOrStreamId.cs` |
