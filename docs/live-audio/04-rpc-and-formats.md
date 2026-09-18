# 04 — RPC and audio formats

This doc covers the wire types, the three container formats, and the RPC
contract that connects the recorder, the server, and the player.

## The audio RPC contract

File: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`.

```csharp
public interface ILiveAudioStreams : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<LiveAudioStreamInfo>> List(Session session, ChatId chatId, CancellationToken ct);

    Task<RpcStream<AudioFrame>?> GetStream(
        Session session, string streamId, TimeSpan skipTo, CancellationToken ct);

    Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
        Session session, string streamId, CancellationToken ct);

    // Streams beginning at/after catchUpFrom (default = none) are served from t=0, not the live edge
    Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session, ChatId chatId, Moment catchUpFrom, CancellationToken ct);
    Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session, ChatId chatId, Moment catchUpFrom, Language? dubLanguage, CancellationToken ct);

    Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
        Session session, ChatId chatId, Moment startAt, TimeSpan rewindOffset, double speed, CancellationToken ct);
    Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
        Session session, ChatId chatId, Moment startAt, TimeSpan rewindOffset, double speed,
        Language? dubLanguage, CancellationToken ct);

    [RpcMethod(RemoteExecutionMode = AwaitForConnection | AllowReconnect, DelayTimeout = double.PositiveInfinity)]
    Task PushStream(
        Session session, string chatId, string? repliedChatEntryId,
        double clientStartAt, int preSkip,
        RpcStream<AudioFrame> frameStream, CancellationToken ct);

    Task ReportAudioLatency(Session session, TimeSpan latency, TimeSpan? avSyncError, CancellationToken ct);

    Task ReportPlayback(Session session, ChatId chatId, string streamId, ChatEntryId? entryId, CancellationToken ct);

    // Legacy methods, kept for already-published clients: LegacyGetListeningStream (no catchUpFrom),
    // LegacyChangeSettings (a no-op), and the 3-argument ReportAudioLatency (its reading is discarded)
}
```

Two subscription shapes for the same audio:

- **`GetStream(streamId, skipTo)`** — per-stream pull, returns
  `RpcStream<AudioFrame>` with optional skip-forward into the buffer.
  Used by the chat-entry-attached audio playback (the played-once-only
  path on a specific message).
- **`GetListeningStream(chatId, catchUpFrom[, dubLanguage])`** — per-chat
  multiplexed feed, returns `RpcStream<MuxedAudioStreamItem>` (a tagged union).
  Used for "Listening" mode where the user is following live audio for an
  entire chat.

`GetReplayStream` is the time-travel variant: `startAt` + `rewindOffset` +
`speed` (1.0–2.0×). Server-side `ReplayStreamMuxer` reads from blob
storage, resolves position, and emits at scaled speed. See
[06-server-fanout-and-replay.md](./06-server-fanout-and-replay.md).

## RPC tuning

The recorder's upload is built on the client, by the TypeScript `MediaRpcStreamOptions`
(`src/nodejs/src/api/api.ts`); the server's delivery streams by the C#
`StandardRpcStream` (`src/dotnet/Api/StandardRpcStream.cs`):

```ts
// MediaRpcStreamOptions (TypeScript)
static audioRecording<T>(): RpcStreamOptions<T> {
    return { isRealTime: false, allowReconnect: true, ackPeriod: AUDIO.stream.recordingRpcStreamAckPeriod }; // 10
}
```

```csharp
// StandardRpcStream (C#)
public static RpcStream<T> NewAudioDelivery<T>(IAsyncEnumerable<T> source, bool allowReconnect = true)
    => new(source) { AllowReconnect = allowReconnect, AckPeriod = Constants.Audio.DeliveryRpcStreamAckPeriod }; // 10
```

Neither sets `AckAdvance`, so both run with RpcStream's default of 61 items in flight.

Comparison with video:

| Parameter | Video | Audio |
|---|---|---|
| Direction | realtime | non-realtime |
| `AckPeriod` (frames) | 5 | 10 |
| Acked interval | ~167 ms (30 fps) | ~200 ms (50 fps) |
| `BufferSize` | 10 (≈333 ms) | not capped explicitly; flow controlled by ACK |
| `canSkipTo` | keyframe | not used (every frame is independently decodable but **never dropped**) |
| `AllowReconnect` | publish: false; subscribe: false | publish: **true**; subscribe: true (false for the listening and replay multiplexes) |
| Loss policy | drop / compact at keyframes | preserve all frames |

The publish-side `AllowReconnect = true` is the headline difference. When
the client's RPC peer changes, the iterator's `return()` fires, the
streamer creates a new `PushStream` call, and resumes from the **oldest
still-buffered frame** in its denque. Server merges by `BeginsAt` so an
in-flight reconnect doesn't produce duplicate audio.

## `AudioFrame` — the wire frame

File: `src/dotnet/Api/Audio/AudioFrame.cs`.

```csharp
[DataContract, MemoryPackable, MessagePackObject]
[MessagePackFormatter(typeof(CachingAudioFrameFormatter))]
public partial class AudioFrame : MediaFrame
{
    public override TimeSpan Offset { get; init; }
    public override TimeSpan Duration { get; init; } = Constants.Audio.OpusFrameDuration;  // 20 ms
    public override bool IsKeyFrame { get; init; } = true;                                  // always
    public ReadOnlyMemory<byte> Data { get; init; }
    public ReadOnlyMemory<byte> SerializedData { get; set; }
}
```

`Offset` is **per-stream**, anchored at stream start (which is the
voice-activity onset, not the recording start). `Duration` is constant
(20 ms). `IsKeyFrame` is constant true.

### `CachingAudioFrameFormatter`

Same role as the video equivalent: serialize-once fan-out. The MessagePack
wire encoding is a 4-key map (`Data`, `Offset`, `Duration`, `IsKeyFrame`).
On deserialize, the bytes are copied into a plain `byte[]` owned by the
frame; `Data` becomes a slice into it. On the publish side, when fanning
out to multiple consumers, the formatter writes the previously-serialized
bytes via `WriteRaw` — no re-encoding per consumer.

## `MuxedAudioStreamItem` — the multiplexed wire union

Files: `src/dotnet/Api/Live/{MuxedAudioStreamItem,MuxedAudioStreamStart,MuxedAudioStreamEnd,MuxedAudioFrame,MuxedAudioStreamReset,LiveAudioStreamInfo}.cs`.

```
MuxedAudioStreamItem (abstract, union-serialized)
├── MuxedAudioStreamStart (#0): { StreamIndex, StreamInfo: LiveAudioStreamInfo, PlaysAt }
├── MuxedAudioStreamEnd   (#1): { StreamIndex }
├── MuxedAudioFrame       (#2): { StreamIndex, Data, Offset }
└── MuxedAudioStreamReset (#3): { } -- never sent by the server: ListeningStreamProcessor
                                     inserts it on reconnect, so the demuxer flushes every stream
```

`StreamIndex` is assigned by the muxer (`ListeningStreamMuxer` or
`ReplayStreamMuxer`) per subscription, so the client demultiplexes by
`StreamIndex` (not `StreamId`). Every `MuxedAudioStreamStart` for a given
`StreamIndex` is followed by zero-or-more `MuxedAudioFrame`s with the same
index, terminated by exactly one `MuxedAudioStreamEnd`.

`LiveAudioStreamInfo` (`Api/Live/LiveAudioStreamInfo.cs`):

```csharp
public sealed partial record LiveAudioStreamInfo
{
    public ChatId ChatId { get; init; }
    public AuthorId AuthorId { get; init; }
    public string StreamId { get; init; }
    public Moment BeginsAt { get; init; }       // server time when first frame arrived
    public AudioFormat? Format { get; init; }
    public ChatEntryId? EntryId { get; init; }
    public Moment SourceBeginsAt { get; init; } // sender's claimed start time
    public bool IsTextOnly { get; init; }       // JustText author: transcribed, never fanned out
    public ApiArray<Language> Languages { get; init; } // the speaker's candidate languages
    public Language? DubLanguage { get; init; } // set only on the start item of a dub track
}
```

## Three container formats (and where each is used)

There are three converters in `src/dotnet/Api/Audio/`:

### 1. ActualOpusStream (live RPC)

- File: `ActualOpusStreamConverter.cs`, `ActualOpusStreamHeader.cs`.
- Header: magic `A_OPUS_S` (8 bytes), version (1 byte), `PreSkip` (int16
  LE), `CreatedAt` ticks (int64 LE).
- Frame framing: `uint16 BE length` + payload bytes, repeated.
- **Where used**: a thin format used to materialise `AudioSource` from a
  byte stream and back. The RPC layer doesn't actually emit the byte
  format on the wire — `RpcStream<AudioFrame>` carries individual
  `AudioFrame` MessagePack objects. The format header is preserved as
  `AudioFormat.CodecSettings` (base64) for clients that need to
  reconstruct codec state from the metadata.

### 2. OggOpusStream (transcription only)

- File: `OggOpusStreamConverter.cs`, `Ogg/*`.
- Standard Ogg/Opus (RFC 7845). Container = Ogg pages, codec = Opus.
- `OpusHead` page (channels, sample rate, pre-skip), `OpusTags` page
  (vendor `"ActualChat Voice"`), then audio pages (frames grouped by
  ~200 ms each).
- One-way — `FromByteStream` throws `NotSupportedException`. We never
  read OggOpus, only write it.
- **Where used**: feeding cloud transcription APIs that take Ogg/Opus
  (`OpenAITranscriber`, the offline/batch Deepgram path). Live Deepgram
  WebSocket and Google Speech V2 take raw Opus packets directly, so this
  converter isn't on the live transcription path.

### 3. WebMStream (blob persistence)

- File: `WebMStreamConverter.cs`, `WebM/*`.
- EBML / Matroska container; codec ID `A_OPUS`.
- Two-way (read + write). Cluster rotation every 30 s for seekability.
- **Where used**: `AudioSegmentSaver.SaveAndCreateMedia` writes
  `AudioRecord/{StreamId}/{streamIndex}.webm` for every recorded segment.
  `AudioSourceDownloader` reads the same file when serving replays.
- Why WebM and not OggOpus for storage? WebM is seekable by timestamp via
  EBML cluster headers, and standard browsers / `<audio>` tags can decode
  it directly without an extra demuxer. OggOpus is just as common but the
  cluster-based seek is more convenient for replay's
  `ResolvePositionInPast/Future`.

The encoded Opus packets are bit-identical across all three formats — only
the framing changes.

## `CachingAudioFrameFormatter` and per-stage serialization

Like the video pipeline, the audio path serializes each frame **exactly
once at server ingress** (when `RpcStream<AudioFrame>` deserializes a
frame's bytes). All downstream fan-out — to other subscribers, to the live
muxer, to the cross-shard cache — emits the same `SerializedData` slice
via `WriteRaw`. The cost of fan-out is a memcpy per consumer plus the
`RpcStream` framing, not full MessagePack encoding.

`OnRecordingStateChange` and similar callback RPCs use normal MessagePack;
only `AudioFrame` gets the caching treatment. `MuxedAudioStreamItem` has no caching
formatter: a `MuxedAudioFrame` carries the frame's bytes as a plain `Data` field, encoded
per subscriber.

## `AudioSource` — the in-memory abstraction

File: `src/dotnet/Api/Audio/AudioSource.cs` and `AudioSourceExt.cs`.

`AudioSource` is the C# representation of an in-flight or stored audio
stream, paired with its `AudioFormat` and a `IAsyncEnumerable<AudioFrame>`.
It is used:

- On the **server** to wrap incoming `RpcStream<AudioFrame>` for
  segmenting, transcription, and persistence.
- In **`AudioSourceDownloader`** to read a `.webm` blob back into frames
  (via `WebMStreamConverter.FromByteStream`).
- In `ReplayStreamMuxer` to seek into stored audio with `SkipTo(timeSpan)`.

Key methods:

- `AudioSource.ReadFromByteStream(IAsyncEnumerable<byte[]>, ...)` — picks
  the converter from the byte stream's first bytes (Ogg / WebM / ActualOpus
  magic).
- `audioSource.SkipTo(TimeSpan)` — fast-forward by Opus frame index.
- `audioSource.WhenDurationAvailable` — completes when the source stream
  ends, exposing `Duration` and (after VAD-aware processing) `AudibleDuration`.

## `AudioRecord`

File: `src/dotnet/Streaming.Contracts/AudioRecord.cs`.

```csharp
public sealed partial record AudioRecord(
    StreamId StreamId, Session Session, ChatId ChatId,
    string? RepliedChatEntryId, double ClientStartAt, int PreSkip)
    : IHasId<StreamId>, IHasNodeRef
```

Built on the API pod inside `LiveAudioStreams.PushStream` and passed to
the backend's `IAudioStreamingBackend.PushAudio`. Like `VideoRecord` it
pins the stream to a node (publisher's backend shard owner).

## Constants worth pinning

| Constant | Value |
|---|---|
| `OpusFrameDuration` | 20 ms |
| `FrameRate` | 50 fps |
| `RecordingSampleRate` | 16 000 Hz |
| `PlaybackSampleRate` | 48 000 Hz |
| `Bitrate` | 32 000 bps |
| `RecordingRpcStreamAckPeriod` | 5 frames (≈100 ms) |
| `DeliveryRpcStreamAckPeriod` | 5 frames |
| `MaxStreamDuration` | 3 min |
| `MaxBeginsAtDrift` | 5 s |
| `FrameSilenceTimeout` | 2 s |
| `StreamExpirationDelay` (StreamStore) | 10 s |

`Constants.Audio.cs` has more (transcription throttle, replay pacing,
playback target buffers, …). [08-diagnostics-and-tuning.md](./08-diagnostics-and-tuning.md)
collects them.
