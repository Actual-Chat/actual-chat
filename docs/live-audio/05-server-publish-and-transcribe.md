# 05 — Server publish path and transcription

This doc covers what happens between the moment `PushStream` lands on the
API pod and the moment a finalised chat entry, a `.webm` blob, and a live
transcript stream are visible to the rest of the system.

## API entry: `LiveAudioStreams.PushStream`

File: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`.

The handler:

1. Allocates a `StreamId` pinned to the current node.
2. Wraps the call's data in an `AudioRecord`.
3. Hands off to `AudioStreamingBackend.PushAudio` (backend service,
   sharded by `ChatId`).

Structurally identical to `LiveVideoStreams.PushStream`. The only audio-
specific argument is `preSkip` (Opus codec warm-up samples).

## Backend: `AudioStreamingBackend.ProcessAudio`

File: `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs`.

This is the longest, most consequential method in the audio pipeline.
It runs three things in parallel:

```
                 ┌──────────────────────┐
                 │ ProcessAudio          │
                 ├──────────────────────┤
                 │ • permission checks  │
                 │ • clock-skew check   │
                 │ • OpenAudioSegment   │
                 │ • register in Live-  │
                 │   AudioBackend        │
                 │ • memoize → publish  │
                 └──────────┬───────────┘
                            │ AudioSource (IAsyncEnumerable<AudioFrame>)
        ┌───────────────────┼───────────────────┐
        ▼                   ▼                   ▼
 ┌────────────┐    ┌─────────────────┐   ┌────────────────┐
 │ Live fan-  │    │ TranscribeAudio │   │ AudioSegment-  │
 │ out via    │    │ (background)    │   │ Saver          │
 │ memoizer   │    │ Google/Deepgram │   │ → WebM blob +  │
 │ → muxer    │    │ → TranscriptDiff│   │   Media record │
 └────────────┘    │ → text entry    │   └────────────────┘
                   │ → ChatsBackend  │
                   └─────────────────┘
```

### 1. Permission checks

```csharp
var rules = await Chats.GetRules(session, chatId, ct);
rules.Require(ChatPermissions.Write);
rules.Require(ChatPermissions.WriteAudio);
```

Same pattern as video. No additional voice-specific permission today.

### 2. Clock-skew check (`MaxBeginsAtDrift = 5 s`)

```csharp
var sourceBeginsAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartAt);
var beginsAt = sourceBeginsAt;
var serverNow = Clocks.ServerClock.Now;
if (Math.Abs((serverNow - beginsAt).TotalSeconds) > 5) {
    Log.LogWarning("ProcessAudio: source clock skew ...");
    beginsAt = serverNow;
}
```

If the publisher's claimed start time is more than 5 s off from the
server clock, the server uses its own clock instead. Same rationale as
video: prevents stale-looking offsets from making the playback path
think the audio is delayed.

### 3. `OpenAudioSegment`

File: `src/dotnet/Streaming.Service/Audio/OpenAudioSegment.cs`.

Constructed on every `PushStream` call:

```csharp
var openSegment = new OpenAudioSegment(
    streamIndex: 0,
    audioRecord: record,
    beginsAt: beginsAt,
    audioSource: audioSource,
    languages: AudioSegmentLanguage.Resolve(...));
```

The segment carries:

- `Source` — the live `AudioSource`.
- `WhenDurationAvailable` — completes when the `AudioSource` finishes.
- `RecordedAt`, `AudibleDuration`, `ClosedSegment` — `AsyncTaskMethodBuilder`s
  filled later.
- `Languages` — list of candidate languages for transcription
  (see `AudioSegmentLanguage.cs`).

There is currently always exactly one segment per recording (`streamIndex: 0`)
because `AudioStream` on the client only emits one stream per voice
segment. The infrastructure supports multiple `streamIndex` values — they
would be saved as `AudioRecord/{streamId}/0001.webm`, `0002.webm`, etc. —
but the recorder doesn't use that capability yet.

### 4. Memoize and publish for fan-out

```csharp
var audioMemoizer = openSegment.Source.ToAudioFrames().Memoize(ct);
if (_audioStreams.Publish(record.StreamId, audioMemoizer))
    publishAudioTask = BackgroundTask.Run(() => audioMemoizer.WhenRunning, ...);
```

`StreamStore<AudioFrame> _audioStreams` (file:
`Services/StreamStore.cs`) is the per-node registry, mirror of the video
side. Memoizer holds the full stream tail (no per-layer trimming —
audio's tail-keep policy is just `ReplayTailSize`/expiration delay).

### 5. Register in `LiveAudioBackend`

```csharp
var streamInfo = new LiveStreamInfo {
    ChatId = chatId,
    AuthorId = author.Id,
    StreamId = streamId.Value,
    BeginsAt = beginsAt,
    Format = audioSource.Format,
    SourceBeginsAt = sourceBeginsAt,
    EntryId = textEntry?.Id,                 // filled in once entry is created
};
await LiveAudioBackend.Register(chatId, streamInfo, ct);
```

Covered in detail in [06-server-fanout-and-replay.md](./06-server-fanout-and-replay.md).

### 6. Frame-silence watchdog

`ProcessAudio` reads from the RPC stream using a per-frame deadline. If no
frame arrives within `Constants.Audio.FrameSilenceTimeout = 2 s`, the
stream is cancelled (and the segment finalised normally — silence
≠ failure). Fast detection of dead clients without giving up on slow ones.

### 7. Stream limit

`Constants.Audio.MaxStreamDuration = 3 min` — beyond this, the stream is
torn down. With VAD-driven segmenting most segments are far shorter; the
3-min ceiling exists to bound resource use on a continuous-talk edge case.

## Persistence — `AudioSegmentSaver`

File: `src/dotnet/Streaming.Service/Services/AudioSegmentSaver.cs`.

Once `openSegment.WhenDurationAvailable` completes (the `AudioSource`
ended), the segment is closed and saved:

```csharp
openSegment.Close(openSegment.Source.Duration);
var closedSegment = await openSegment.ClosedSegment;
var media = await SaveAndCreateMedia(closedSegment, beginsAt, ct);
audioMediaIdTcs.SetResult(media.Id);
```

`SaveAndCreateMedia`:

1. Construct `WebMStreamConverter`.
2. `var byteStream = converter.ToByteStream(audioSource, ct)` — returns
   an async byte stream of the WebM-wrapped Opus.
3. `blobStorage.UploadByteStream(blobId, byteStream, ct)` — uploads to
   `BlobScope.AudioRecord` storage. Implementation depends on deployment
   (S3, GCS, local disk, …).
4. `Commander.Call(new MediaBackend_Change(mediaId, null, Change.Create(media)))`
   creates a `MediaFull` record:

   ```csharp
   new MediaFull(mediaId) {
       BlobId = blobId,
       ContentType = "audio/webm",
       BeginsAt = beginsAt,
       EndsAt = beginsAt + closedSegment.Duration,
       ContentEndsAt = beginsAt + closedSegment.AudibleDuration,
       ClientSideBeginsAt = recordedAt,
   }
   ```

   `AudibleDuration` is the duration with leading/trailing silence
   removed — used by replay timing (player can skip the tails) and by
   chat entry's `EndsAt` so a long pause at the end doesn't pad the
   entry's wallclock duration.

## Transcription — `TranscribeAudio`

Spawned as a background task with `CancellationToken.None` so it
completes even if the publisher disconnects:

```csharp
var transcribeTask = BackgroundTask.Run(
    () => TranscribeAudio(openSegment, beginsAt, liveStreamId,
                          audioMediaIdTcs.Task, CancellationToken.None),
    Log, $"{nameof(TranscribeAudio)} failed", CancellationToken.None);
```

### Provider selection

`TranscriberFactory` (`Services/Transcribers/TranscriberFactory.cs`):

```csharp
if (Settings.UseFakeTranscriber) return services.GetRequiredService<FakeTranscriber>();
return engine switch {
    TranscriptionEngine.Deepgram => services.GetRequiredService<DeepgramTranscriber>(),
    _ => services.GetRequiredService<GoogleTranscriber>(),
};
```

- **Google** is the default for known languages.
- **Deepgram** is forced when `transcriptionOptions.DetectLanguage` is
  true (auto-language detection); also used when configured per-chat.
- **Fake** is for tests — it emits canned word patterns synced to frame
  offsets.

### Language handling

`AudioSegmentLanguage.Resolve(...)` figures out the candidate languages:

- Chat has explicit language → use that.
- User has spoken-language preferences → those are candidates.
- Otherwise → auto-detect (via Deepgram's `language: "multi"`).

When auto-detection runs, the first reasonably-confident detection fires
an `onLanguageDetected` callback that records the language on the chat
entry.

### Google Speech v2 (`GoogleTranscriber.cs`)

- gRPC streaming via `Google.Cloud.Speech.V2`.
- Model: `"long"` (optimised for longer audio).
- Features: automatic punctuation, word confidence, word time offsets,
  interim results.
- Streams 16 kHz Opus directly (no OggOpus needed).

### Deepgram (`DeepgramTranscriber.cs`)

- WebSocket via `ListenWebSocketClient`.
- Model: `"nova-3"` if all candidate languages are nova-3-supported, else
  `"nova-2"`.
- Features: punctuation, smart formatting, interim results,
  end-pointing 100 ms.
- Auto-language: `language: "multi"`.

### Output: `TranscriptDiff` stream

```csharp
using var transcripts = transcriber.Transcribe(streamId.Value, audioSource, opts, ct)
    .ThrottleTranscript(Constants.Transcription.ThrottlePeriod, ...)  // 0.2 s
    .Memoize(CancellationToken.None);

await foreach (var transcript in transcripts.Replay(ct)) {
    if (EmptyRegex.IsMatch(transcript.Text)) continue;
    if (textEntry == null) {
        // First non-empty transcript:
        var transcriptDiffStream = transcripts.Replay(ct).ToTranscriptDiffs().Memoize(ct);
        _transcriptStreams.Publish(transcriptStreamId, transcriptDiffStream);
        textEntry = await CreateTextEntry(transcript);
    }
    // ... language detection callbacks ...
}
```

Two things happen on the **first non-empty transcript**:

1. The transcript stream gets memoised and published into a separate
   `_transcriptStreams: StreamStore<TranscriptDiff>`. Subscribers fetch it
   via `ILiveAudioStreams.GetTranscriptStream(streamId)`.
2. A chat entry is created via `ChatsBackend_ChangeEntry`:

   ```csharp
   new ChatEntryDiff {
       AuthorId = authorId,
       Content = "",
       ContentStreamId = transcriptStreamId.Value,    // points to live transcript
       Audio = liveStreamId != null ? new ChatEntryAudio { StreamId = liveStreamId } : null,
       BeginsAt = beginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
       RepliedEntryLid = repliedEntryId?.LocalId,
   }
   ```

   The `ContentStreamId` points readers at the transcript diff stream
   for live captions. The `Audio.StreamId` points at the live audio
   stream — readers can listen too.

### Finalisation

```csharp
finally {
    if (lastTranscript != null && textEntry != null)
        await Task.WhenAll(FinalizeTextEntry(), FinalizeLanguages());
}
```

`FinalizeTextEntry` updates the chat entry with:

- `Content = lastTranscript.Text` (full final text).
- `ContentStreamId = ""` (no more streaming).
- `Audio = new ChatEntryAudio { MediaId = audioMediaId, TimeMap = lastTranscript.TimeMap }`
  — replaces the live `StreamId` with the saved blob's `MediaId` and a
  word-timestamp map for click-to-play.
- `EndsAt = beginsAt + TimeSpan.FromSeconds(lastTranscript.TimeRange.End)`.

If the transcript ended up empty (silence segment), the entry is
removed instead.

## Cancellation behaviour

`ProcessAudio` runs under the publisher's RPC cancellation token. The
transcription background task uses `CancellationToken.None` deliberately
so a publisher disconnect doesn't kill an in-progress transcription.
There's a 3 s grace (`Constants.Transcription.CancellationDelay`) on
the cancel propagation path so transcribers can finish flushing.

## Where the transcript stream lives

`StreamStore<TranscriptDiff> _transcriptStreams` — same generic
`StreamStore` as the audio frames, just typed for diffs. It's per-node
(publisher's shard); cross-shard subscribers go through `RemoteStream-
Caches` exactly like audio.

## Telemetry

- `AppMeters.AudioStreamCount` — UpDownCounter, increments on
  `_audioStreams.Publish`, decrements on expire.
- `AppMeters.AudioLatency` — recorded from
  `ILiveAudioStreams.ReportAudioLatency` (receiver-reported end-to-end
  latency).
- Logs at info: `"ProcessAudio: source clock skew ..."` (clock-skew
  override), `"Register: evicting stale stream ..."` (per-author merge
  evictions), transcription provider/model on each segment.
