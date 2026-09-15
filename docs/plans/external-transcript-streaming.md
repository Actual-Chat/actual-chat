# External Transcript Streaming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let integrations stream a producer-supplied live transcript alone or together with audio, while preserving native audio streaming with Voxt transcription and ordinary streamed markup messages.

**Architecture:** Reconcile the existing `feat/text-entry-streaming` branch for ordinary markup/LLM messages. Add an explicit external-transcript producer path to `ILiveAudioStreams`: the producer sends incremental text with source-audio offsets and optional audio frames; the backend builds `TranscriptDiff`/`LinearMap`, publishes both streams under one stream ID, and finalizes either a plain text entry or a playable `ChatEntryAudio`. Server transcription is bypassed only in this explicit mode.

**Tech Stack:** C# 15, ActualLab.Rpc streams, ActualChat streaming/audio/transcription services, Fusion, xUnit, Blazor/TypeScript.

**Spec:** [Chat migration and external streaming](./chat-migration-and-streaming.md)

## Global Constraints

- Existing `ILiveAudioStreams.PushStream` wire behavior remains compatible and continues using server transcription.
- Ordinary streamed messages use `IChats.StreamEntry` from `feat/text-entry-streaming` and support markup.
- External transcript streams are plain transcript content; markup is not parsed during the live voice/transcript path.
- Network arrival time is never used as transcript timing. Every text delta carries a source media offset.
- Audio plus transcript finalizes only with a valid complete time map. Invalid input fails visibly.
- Maintenance mode blocks all new external streams in an affected chat.
- A stream always attributes content to the authenticated caller; import impersonation is unavailable here.

## Reuse

### Existing abstractions to reuse

- `IChats.StreamEntry`, `TextEntryStreamer`, partial markup rendering, and tests on `feat/text-entry-streaming`.
- `ILiveAudioStreams.PushStream`, `AudioStreamingBackend.ProcessAudio`, `AudioRecord`, `OpenAudioSegment`, and live registration/fan-out.
- `IAudioStreamingBackend.PushTranscript`, `Transcript`, `TranscriptDiff`, `LinearMap`, `ChatEntryAudio`, and stream fixup.
- Imported-audio final validation from [Consented chat import](./consented-chat-import.md).

### Reusability of new components

- Incremental offset-to-time-map building belongs in `ActualChat.Api/Transcription`, shared by live
  integrations, imports, and future transcription providers.
- External stream coordination belongs in `Streaming.Service`, beside native audio processing.
- Do not put transport logic in MCP tools. MCP adapters call stable chat/streaming contracts.

---

### Task 1: Reconcile ordinary streamed markup messages

**Files:**
- Rebase/merge: changes from `feat/text-entry-streaming`
- Review: `src/dotnet/Api.Contracts/Chat/IChats.cs`
- Review: `src/dotnet/Chat.Service/TextEntryStreamer.cs`
- Review: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageInternalView.razor`
- Test: `tests/Chat.IntegrationTests/TextEntryStreamerTest.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/StreamingMarkupExtTest.cs`

**Interfaces:**
- Produces: `IChats.StreamEntry(Session, ChatId, long?, RpcStream<string>, CancellationToken)` for ordinary streamed text.

- [ ] **Step 1: Rebase the branch onto current `dev` and resolve entry/provenance changes explicitly**

Do not reimplement its `TextEntryStreamer`. Preserve its 200 ms coalescing, finalization-on-failure,
recent-message streamed edits, ordinary fallback for old edits, and partial-markup rendering.

- [ ] **Step 2: Add maintenance-guard coverage**

Write a focused test proving a new stream and streamed edit are rejected while the chat is maintained.

- [ ] **Step 3: Run existing branch tests and `npm run build:Verify`**

- [ ] **Step 4: Commit the reconciled branch**

Use the final PR/branch history policy rather than copying old commit hashes into the new work.

### Task 2: Define external transcript input contracts

**Files:**
- Create: `src/dotnet/Api/Transcription/ExternalTranscriptChunk.cs`
- Modify: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`
- Modify: AOT source declarations under `src/dotnet/Api*/Module/`
- Test: `tests/Streaming.UnitTests/ExternalTranscriptContractTest.cs`

**Interfaces:**
- Produces: timestamped incremental transcript chunks and two explicit methods:

```csharp
Task<ChatEntry> PushTranscriptStream(
    Session session,
    ChatId chatId,
    ChatEntryId? repliedEntryId,
    double clientStartAt,
    RpcStream<ExternalTranscriptChunk> transcriptStream,
    CancellationToken cancellationToken);

Task<ChatEntry> PushAudioWithTranscript(
    Session session,
    ChatId chatId,
    ChatEntryId? repliedEntryId,
    double clientStartAt,
    int preSkip,
    RpcStream<AudioFrame> frameStream,
    RpcStream<ExternalTranscriptChunk> transcriptStream,
    CancellationToken cancellationToken);
```

- [ ] **Step 1: Write failing serialization and compatibility tests**

`ExternalTranscriptChunk` carries an append/replace text delta, the source audio offset at the end of
the resulting text, and stability. Require finite, nonnegative offsets and bounded text growth.

- [ ] **Step 2: Verify old `PushStream` contract snapshots remain unchanged**

- [ ] **Step 3: Add contracts as new RPC methods, not a signature replacement**

- [ ] **Step 4: Run tests and commit**

```powershell
git add src/dotnet/Api/Transcription src/dotnet/Api.Contracts/Streaming tests/Streaming.UnitTests/ExternalTranscriptContractTest.cs
git commit -m "feat(streaming): define external transcript input"
```

### Task 3: Build transcript and time map incrementally

**Files:**
- Create: `src/dotnet/Api/Transcription/ExternalTranscriptBuilder.cs`
- Test: `tests/Streaming.UnitTests/ExternalTranscriptBuilderTest.cs`

**Interfaces:**
- Consumes: ordered `ExternalTranscriptChunk` values.
- Produces: validated `TranscriptDiff` values and final `Transcript`.

- [ ] **Step 1: Write failing builder tests**

Cover append, correction/replacement, stable chunks, equal offsets, decreasing offsets, text shrink,
Unicode boundaries, empty chunks, cancellation, final map coverage, maximum duration, and maximum
message length. Include fragmented inputs resembling LLM tokens and speech recognizer corrections.

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/Streaming.UnitTests/Streaming.UnitTests.csproj --no-restore --filter ExternalTranscriptBuilderTest`

- [ ] **Step 3: Implement the builder using `Transcript` and `LinearMapDiff`**

Build mapping points from producer offsets, not server clocks. Coalesce outbound diffs to the same
cadence used by live transcripts while retaining the latest correction and stability marker.

- [ ] **Step 4: Validate the final result with the shared imported-audio validator**

For transcript-only mode validate text/map structure without requiring media duration. For audio
mode additionally constrain the final map to the finalized audio duration.

- [ ] **Step 5: Run tests and commit**

```powershell
git add src/dotnet/Api/Transcription/ExternalTranscriptBuilder.cs tests/Streaming.UnitTests/ExternalTranscriptBuilderTest.cs
git commit -m "feat(transcription): build external transcript maps"
```

### Task 4: Stream external transcript without audio

**Files:**
- Create: `src/dotnet/Streaming.Service/Services/ExternalTranscriptStreamer.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Modify: `src/dotnet/Streaming.Service/Module/StreamingServiceModule.cs`
- Test: `tests/Streaming.IntegrationTests/ExternalTranscriptStreamTest.cs`

**Interfaces:**
- Consumes: transcript builder, `IAudioStreamingBackend.PushTranscript`, chat author/rules, and maintenance guard.
- Produces: live plain transcript fan-out and a finalized text `ChatEntry` with no audio metadata.

- [ ] **Step 1: Write failing live/finalization tests**

Assert the entry appears immediately, readers receive growing transcript diffs, live presence is
registered, final content is plain text, `ContentStreamId` clears, `Audio` is null, replies work,
authenticated author is enforced, and disconnect/failure finalizes safely.

- [ ] **Step 2: Run the focused integration test and verify it fails**

- [ ] **Step 3: Implement transcript-only coordination**

Create the entry and stream ID before draining input. Publish built diffs through
`IAudioStreamingBackend.PushTranscript`. Register the stream as text-only, then finalize content and
unregister it in `finally`, following native stream/fixup behavior.

- [ ] **Step 4: Run tests and commit**

```powershell
git add src/dotnet/Streaming.Service tests/Streaming.IntegrationTests/ExternalTranscriptStreamTest.cs
git commit -m "feat(streaming): accept external live transcripts"
```

### Task 5: Pair external transcript with live audio

**Files:**
- Modify: `src/dotnet/Streaming.Service/Services/ExternalTranscriptStreamer.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Refactor: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs`
- Test: `tests/Streaming.IntegrationTests/ExternalAudioTranscriptStreamTest.cs`
- Test: `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs`

**Interfaces:**
- Consumes: normal audio ingestion/persistence plus external transcript diffs sharing client start time and audio offsets.
- Produces: live audio and transcript under one stream ID and finalized playable `ChatEntryAudio`.

- [ ] **Step 1: Write failing synchronization and compatibility tests**

Cover audio/transcript at different rates, transcript corrections, either stream ending first,
disconnect of either sender, audio shorter/longer than final map, invalid offset, server-ASR bypass,
live listener playback, persisted replay, and unchanged native `PushStream` behavior.

- [ ] **Step 2: Run the focused tests and verify they fail**

- [ ] **Step 3: Extract common audio persistence/fan-out from `ProcessAudio`**

Reuse native audio validation, permission checks, registration, Opus persistence, and cleanup. Select
only the transcript producer: existing ASR for `PushStream`, `ExternalTranscriptBuilder` for the new
method. Do not duplicate `OpenAudioSegment` or media finalization.

- [ ] **Step 4: Coordinate both RPC streams**

Drain audio and transcript concurrently with linked cancellation. Disconnect both remote senders on
completion. The transcript's offsets are on the audio source timeline; validate against frame offsets
and final duration before committing the playable entry.

- [ ] **Step 5: Run focused tests and commit**

```powershell
git add src/dotnet/Streaming.Service tests/Streaming.IntegrationTests/ExternalAudioTranscriptStreamTest.cs tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs
git commit -m "feat(streaming): pair external transcript with audio"
```

### Task 6: Expose integration-friendly adapters and documentation

**Files:**
- Modify: `src/dotnet/Mcp/Tools/McpMessageTools.cs`
- Add or modify: the public API adapter used for streaming integrations
- Modify: English streaming documentation in sibling `ActualChat-docs`
- Test: `tests/Mcp.IntegrationTests/McpMessageToolsTest.cs`

**Interfaces:**
- Consumes: stable `IChats.StreamEntry` and external streaming contracts.
- Produces: documented integration paths without exposing backend stream IDs.

- [ ] **Step 1: Decide adapter shape from protocol capabilities**

For transports that can carry RPC streams, expose the streaming calls directly. For MCP, whose tool
arguments are request/response values, provide start/append/finish text-stream tools backed by a
bounded server-side stream lease; do not buffer arbitrary audio in MCP JSON calls.

- [ ] **Step 2: Write failing MCP lifecycle tests**

Cover authenticated start/append/finish, ownership, expiry, idempotent retries, maintenance rejection,
and abandoned-stream finalization.

- [ ] **Step 3: Implement the thin adapter and document all three producer modes**

Document: native audio + Voxt ASR; ordinary streamed markup; external transcript only; external
audio + transcript. Include offset/time-map rules and complete examples.

- [ ] **Step 4: Run MCP/streaming tests and commit**

```powershell
git add src/dotnet/Mcp tests/Mcp.IntegrationTests/McpMessageToolsTest.cs
git commit -m "feat(mcp): expose streaming text lifecycle"
```

### Task 7: Final verification

- [ ] Run all `TextEntryStreamer`, `ExternalTranscript`, `ExternalAudioTranscript`, and maintenance streaming tests.
- [ ] Run existing live audio, translation streaming, replay, network-loss, and streaming-fixup suites.
- [ ] Run `dotnet build ActualChat.CI.slnf --no-restore` when available.
- [ ] Run `npm run build:Verify`.
- [ ] Drive a real two-client session with transcript-only and audio-plus-transcript producers; confirm live display, muted autoplay suitability, final replay, word highlighting, and translation.
