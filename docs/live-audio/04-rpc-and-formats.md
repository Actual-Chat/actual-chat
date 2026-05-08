# 04 — RPC and audio formats

This doc covers the wire types, the three container formats, and the RPC
contract that connects the recorder, the server, and the player.

## The audio RPC contract

File: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`.

```csharp
public interface ILiveAudioStreams : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<LiveStreamInfo>> List(Session session, ChatId chatId, CancellationToken ct);

    Task<RpcStream<AudioFrame>?> GetStream(
        Session session, string streamId, TimeSpan skipTo, CancellationToken ct);

    Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
        Session session, string streamId, CancellationToken ct);

    [RpcMethod(RemoteExecutionMode = AwaitForConnection | AllowReconnect)]
    Task PushStream(
        Session session, string chatId, string? repliedChatEntryId,
        double clientStartAt, int preSkip,
        RpcStream<AudioFrame> frameStream, CancellationToken ct);

    Task ReportAudioLatency(Session session, TimeSpan latency, CancellationToken ct);

    [LegacyName("GetStream", "2.7.9999")]
    Task<RpcStream<LiveStreamItem>> LegacyGetStream(
        Session session, ChatId chatId, LiveStreamSettings settings, CancellationToken ct);

    Task ChangeSettings(Session session, ChatId chatId, LiveStreamSettings settings, CancellationToken ct);

    Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session, ChatId chatId, Moment startAt, TimeSpan rewindOffset, double speed, CancellationToken ct);
}
```

Two subscription shapes for the same audio:

- **`GetStream(streamId, skipTo)`** — per-stream pull, returns
  `RpcStream<AudioFrame>` with optional skip-forward into the buffer.
  Used by the chat-entry-attached audio playback (the played-once-only
  path on a specific message).
- **`LegacyGetStream(chatId, settings)`** — per-chat multiplexed feed,
  returns `RpcStream<LiveStreamItem>` (a tagged union). Used for
  "Listening" mode where the user is following live audio for an entire
  chat. Despite the name, this is the **active** path for live group
  listening.

`GetReplayStream` is the time-travel variant: `startAt` + `rewindOffset` +
`speed` (1.0–2.0×). Server-side `ReplayStreamMuxer` reads from blob
storage, resolves position, and emits at scaled speed. See
[06-server-fanout-and-replay.md](./06-server-fanout-and-replay.md).

## RPC tuning

```csharp
// MediaRpcStreamOptions.cs
public static RpcStream<T> AudioRecording<T>(IAsyncEnumerable<T> source)
    => new(source) { AckPeriod = Constants.Audio.RecordingRpcStreamAckPeriod };  // 5

public static RpcStream<T> AudioDelivery<T>(IAsyncEnumerable<T> source, bool allowReconnect = true)
    => new(source) { AllowReconnect = allowReconnect, AckPeriod = Constants.Audio.DeliveryRpcStreamAckPeriod };  // 5
```

Comparison with video:

| Parameter | Video | Audio |
|---|---|---|
| Direction | realtime | non-realtime |
| `AckPeriod` (frames) | 5 | 5 |
| Acked interval | ~167 ms (30 fps) | ~100 ms (50 fps) |
| `BufferSize` | 10 (≈333 ms) | not capped explicitly; flow controlled by ACK |
| `canSkipTo` | keyframe | not used (every frame is independently decodable but **never dropped**) |
| `AllowReconnect` | publish: false; subscribe: false | publish: **true**; subscribe: true |
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

## `LiveStreamItem` — the multiplexed wire union

Files: `src/dotnet/Api/Live/{LiveStreamItem,LiveStreamStart,LiveAudioFrame,LiveStreamEnd,LiveStreamReset,LiveStreamInfo}.cs`.

```
LiveStreamItem (abstract, union-serialized)
├── LiveStreamStart  (#0): { StreamIndex, LiveStreamInfo, PlaysAt }
├── LiveStreamEnd    (#1): { StreamIndex }
├── LiveAudioFrame   (#2): { StreamIndex, Data, Offset }
└── LiveStreamReset  (#3): { } -- chat-level reset, defined but not currently emitted
```

`StreamIndex` is assigned by `LiveStreamMuxer` per subscription, so the
client demultiplexes by `StreamIndex` (not `StreamId`). Every
`LiveStreamStart` for a given `StreamIndex` is followed by zero-or-more
`LiveAudioFrame`s with the same index, terminated by exactly one
`LiveStreamEnd`.

`LiveStreamInfo` (`Api/Live/LiveStreamInfo.cs`):

```csharp
public sealed partial record LiveStreamInfo
{
    public ChatId ChatId { get; init; }
    public AuthorId AuthorId { get; init; }
    public string StreamId { get; init; }
    public Moment BeginsAt { get; init; }       // server time when first frame arrived
    public AudioFormat? Format { get; init; }
    public ChatEntryId? EntryId { get; init; }
    public Moment SourceBeginsAt { get; init; } // sender's claimed start time
}
```

`LiveStreamSettings` (passed to `LegacyGetStream`):

- Currently just toggles whether the muxer should also include the user's
  own audio. Always `false` in production; debug pages can enable
  self-listen.

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
only `AudioFrame` and `LiveStreamItem` get the caching treatment.

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
