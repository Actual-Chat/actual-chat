# Media Pipeline Improvement Candidates

This document captures architectural refactoring candidates found by comparing
the live audio and video pipelines end to end. The goal is not to collapse audio
and video into one generic pipeline. They have different media semantics:
audio frames are independently decodable and playback is audio-clock driven,
while video has keyframe dependencies, simulcast layers, and explicit
presentation-buffer policy.

The goal is to extract the repeated primitives and policies so the pipeline code
gets smaller, easier to read, more consistent, and easier to test.

## Main Theme

Prefer shared policy primitives over a shared "media pipeline" framework.

Good shared abstractions:

- timeline/source-time helpers
- encoded pre-decode buffers with pluggable skip policy
- replaceable slots
- typed sample/byte buffer helpers
- RPC stream option factories
- worker lifecycle helpers
- small testable quality-control classifiers

Poor abstraction target:

- a single base class or orchestrator for all audio/video capture, transport,
  decode, and render behavior.

Audio and video should stay domain-specific at source, codec, and renderer
boundaries.

## High-Value Extractions

### 1. Encoded Pre-Decode Buffer

Both receive paths have an encoded buffer immediately before decode:

- video: `EncodedChunkBuffer` in
  `src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker.ts`
- audio: `EncodedFrameBuffer` in
  `src/dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder.ts`

They share the same broad shape:

- store encoded frames in source-time order
- compute buffered duration
- release only when target duration is reached
- clear on reset/config change
- support explicit catch-up decisions

They differ in skip policy:

- video may skip only to decoder-safe frames, normally keyframes
- audio may skip to any frame and may temporarily speed up by dropping every Nth
  frame

Candidate abstraction:

```ts
interface TimedMediaItem {
    readonly sourceOffsetMs: number;
    readonly durationMs: number;
}

interface EncodedMediaBufferPolicy<T> {
    readonly targetDurationMs: () => number;
    getStartMs(item: T): number;
    getDurationMs(item: T): number;
    canSkipTo?(item: T): boolean;
    isEnd?(item: T): boolean;
}
```

Then provide policy-specific wrappers:

- `VideoEncodedBuffer` uses keyframe-aware trim when duration exceeds target.
- `AudioEncodedBuffer` uses arbitrary-frame skip and speed-up commands.

This makes buffer behavior independently testable without WebCodecs,
AudioDecoder, workers, RPC, or Blazor interop.

### 2. Replaceable Slot

Video currently repeats the "latest item wins" pattern in multiple places:

- raw-frame processing slot in `video-processing.ts`
- encoder input slot in `video-processing.ts`
- decoded presentation slot in `video-player.ts`
- worker MSTG selector has a similar pending-frame slot

Extract:

```ts
class ReplaceableSlot<T> {
    get value(): T | null;
    get replacementCount(): number;
    replace(item: T): T | null;
    take(): T | null;
    clear(): void;
}
```

For `VideoFrame` and similar objects, use a configured disposer:

```ts
new ReplaceableSlot<VideoFrame>({ dispose: frame => frame.close() });
```

This centralizes:

- closing replaced frames
- replacement metrics
- "slot occupied" checks
- test coverage for accidental leaks

### 3. Byte and Typed-Array Buffer Helpers

`ownedArrayBuffer` is duplicated in the main-thread video player and decoder
worker. Move it into a shared media utility module.

Also reconsider `AudioRingBuffer`. The repo already has a generic TS
`RingBuffer<T>` ported from .NET, while audio has a separate multi-channel
sample ring. The sample ring is valid as a separate primitive, but it should be
renamed and documented as a typed sample buffer, not as a generic ring buffer.

Suggested primitives:

- `ownedArrayBuffer(view: Uint8Array): ArrayBuffer`
- `SampleRingBuffer` for mono/multi-channel `Float32Array`
- `PooledArrayBufferLease` or small helper around `ObjectPool<ArrayBuffer>`

### 4. Media Timeline Helpers

Current names are mixed:

- `clientStartOffset`
- `sourceStartedAtMs`
- `startedAtMs`
- `recordedAtMs`
- `sourceRecordedAtMs`
- `capturedAtMs`
- `sourceOffsetMs`

Suggested internal vocabulary:

- `sourceStartedAtMs`: absolute source-clock anchor
- `sourceOffsetMs`: media offset from source start
- `sourceTimeMs`: `sourceStartedAtMs + sourceOffsetMs`
- `presentationLagMs`: `nowMs - sourceTimeMs`
- `capturedAtMs`: only at the capture boundary, before stream anchoring

Keep RPC names such as `clientStartOffset` at service boundaries for
compatibility, but convert them into source-time names immediately inside the
pipeline.

Candidate helper:

```ts
interface MediaTimeline {
    readonly sourceStartedAtMs: number;
    sourceTimeMs(sourceOffsetMs: number): number;
    presentationLagMs(sourceOffsetMs: number, nowMs: number): number;
}
```

The same concept can exist in .NET for server-side skew validation and stream
registration.

## Consistency and Robustness Issues

### 1. Audio Recording Path Still Has Silent Drop-Oldest Points

The target audio pipeline says the recording/upload path should preserve speech
and should not intentionally skip recorded frames. Current code still has
drop-oldest behavior:

- encoder worker sample queue drops over `AUDIO.encode.maxBufferedFrames`
- audio streamer queue drops over `AUDIO.stream.maxBufferedFrames`

This should become an explicit policy decision:

- `LosslessHandoff`: no intentional drop; expose backpressure/failure if
  sustained overload occurs
- `DropOldestPreRoll`: allowed only for voice-start pre-roll before speech is
  committed to recording
- `PlaybackCatchUp`: allowed only on receiver playback side

Even if the first implementation keeps a cap for safety, the code should name it
as an overload failure path rather than normal recording policy.

### 2. RPC Stream Options Are Scattered

Video explicitly uses real-time `RpcStream` settings with `canSkipTo` keyframe
logic. Audio currently uses a mix of defaults and legacy server constants.

Create named factories:

```csharp
public static class MediaRpcStreamOptions
{
    public static RpcStream<T> VideoRealtime<T>(
        IAsyncEnumerable<T> source,
        Func<T, bool> canSkipTo);

    public static RpcStream<T> AudioRecording<T>(IAsyncEnumerable<T> source);
    public static RpcStream<T> AudioDelivery<T>(IAsyncEnumerable<T> source);
    public static RpcStream<T> TranscriptDelivery<T>(IAsyncEnumerable<T> source);
}
```

And TS equivalents for client-created streams.

This makes it hard to accidentally apply real-time compaction to audio or forget
keyframe compaction for video.

### 3. Server Push/Pull Lifecycle Is Similar But Repeated

Audio and video service methods repeat a lot of lifecycle shape:

- parse IDs
- create stream ID
- validate permissions
- create record
- wrap incoming `RpcStream`
- publish to backend
- register/unregister live stream
- disconnect producer stream in `finally`
- apply cancellation/watchdog policy

Extract helper methods, not a base service hierarchy. A small
`LiveMediaStreamServiceHelper` or extension methods can remove repetition while
leaving audio/video service code explicit.

### 4. Server Retention Policy Should Be Explicit

Video now has `VideoStreamMemoizer` for keyframe-span retention. Audio uses
`StreamStore<AudioFrame>` with different expected semantics.

Consider making retention policy explicit:

- audio live recording: full retention while live / until expiration
- video live replay: duration-bounded keyframe-span retention
- transcript: full stream or transcript-specific retention

This can be modeled as:

```csharp
public interface IStreamRetentionPolicy<T>
{
    AsyncMemoizer<T> CreateMemoizer(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken);
}
```

This keeps `StreamStore<T>` generic and makes media-specific retention testable.

## Worker-Level Improvements

Several workers repeat the same setup steps:

- `initAppConstants`
- `Versioning.init`
- WASM/module loading with retry
- RPC server/client wiring
- `Api.init`
- connectivity forwarding
- connect/disconnect debug hooks
- log deduplication and cooldowns

Create small utilities rather than one worker superclass:

- `createWorkerRpcServer`
- `initWorkerConstantsAndVersioning`
- `createEmscriptenLoaderOptions`
- `connectWorkerApi`
- `ErrorDeduper`
- `CooldownGate`

This would remove repeated TODOs like "create wrapper around module for all
workers" and make worker setup failures easier to test.

## Quality-Control Extraction

The target video docs already describe clean recording/playback quality control
loops. The implementation should keep moving classifier/allocation logic into
pure modules:

- recording health classifier
- playback stream verdict classifier
- aggregate health calculator
- capacity estimator
- quality allocator
- cooldown/hysteresis policy

These should have deterministic unit tests with synthetic windows. Worker and
Blazor code should only collect signals and apply decisions.

## Testing Strategy

Start with pure tests before integration tests.

Recommended unit-test targets:

- `EncodedMediaBuffer`
  - releases only after target duration
  - video trim keeps first remaining item decoder-safe
  - audio skip drops arbitrary old frames
  - audio speed-up drops every Nth frame until target
  - end sentinel drains immediately
  - clear resets skip/speed-up state
- `ReplaceableSlot`
  - replacing closes/disposes old item
  - `take` transfers ownership
  - counters update correctly
- `MediaTimeline`
  - source-time and lag calculations
  - server-clock offset handling
  - skew fallback behavior, if mirrored in .NET
- `RpcStream` policy factories
  - video real-time stream has keyframe `canSkipTo`
  - audio recording stream has no skip predicate
  - audio delivery settings match constants
- quality-control pure modules
  - stable healthy windows step up slowly
  - bad windows step down quickly
  - neutral windows hold
  - reconnect clears transient windows without losing last stable request

## Suggested Refactoring Order

1. Extract low-risk utilities:
   `ownedArrayBuffer`, `ErrorDeduper`, `CooldownGate`.
2. Extract `ReplaceableSlot` and port presentation/encoder slots.
3. Extract `EncodedMediaBuffer` and port audio/video pre-decode buffers.
4. Introduce `MediaTimeline` naming and helpers; convert internal TS fields
   toward `sourceStartedAtMs` and `sourceOffsetMs`.
5. Introduce named RPC stream option factories in TS and .NET.
6. Make audio recording handoff policies explicit and remove silent
   drop-oldest behavior from committed speech paths.
7. Move quality-control classifiers/allocation into pure modules with tests.
8. Add server retention policy abstractions if `StreamStore<T>` keeps gaining
   media-specific behavior.

## Guiding Rule

If two pieces differ only by policy, extract the primitive and pass the policy.
If they differ by media semantics, keep them separate and make the semantic
boundary obvious.
