# Audio Pipeline

This document describes the target high-level design of the live audio pipeline.
It is intentionally conceptual: it names the big parts of the pipeline and the
buffering responsibilities between them, without tying the design to current
files, classes, or implementation details.

Audio differs from video in three ways that shape this design:

- Every audio frame is independently decodable. There is no keyframe
  dependency, so any frame is a valid playback skip target.
- Audio frame size is small and the frame rate is high (a 20 ms Opus frame is
  on the order of 80 bytes), so encoded audio bandwidth is not a constraint
  worth adapting to.
- Audio output is sample-rate locked. Once playback starts, the platform
  audio clock advances at a fixed rate; corrections happen by skipping or
  extending the buffer, not by changing playback rate.

Because of these properties, audio has **no control plane**. There is no
runtime adaptation of bitrate, codec, or layer; quality choice happens once
at stream creation and does not change.

The recording/upload side and playback side intentionally have different
semantics:

- Recording/upload is loss-preserving for speech. Once audio is considered
  part of a recording, it should be streamed to the server as quickly as
  possible and should not be intentionally skipped, compacted, or dropped.
  The server needs the complete audio for transcription and persistence.
- Playback is allowed to skip audio frames because audio has no keyframe
  dependency. Playback skipping is a presentation decision, not an upload
  decision.

## Goals

- Keep live audio latency low, predictable, and bounded.
- Preserve all recorded speech on the sender-to-server path for transcription
  and persistence.
- Make `audio buffer` responsible for intentional buffering.
- Minimize latency everywhere else in the pipeline.
- Treat intermediate queues as short-lived fluctuation absorbers, not playback
  latency.
- Do not use sender-side skip/compaction as a recording upload policy.
- Allow receiver-side playback to skip at any audio frame when presentation
  synchronization requires it.
- Keep audio aligned with video presentation through a shared origin
  timeline.

## Buffering Concepts

The pipeline distinguishes four concepts:

- `lossless handoff`: a short handoff queue before a component that must not
  intentionally skip recorded speech. "Lossless" refers to the handoff
  policy — no frame is intentionally dropped or replaced inside this queue —
  not to the audio codec, which is itself lossy. The queue is drained as
  quickly as possible. If sustained overload happens, the system should
  surface backpressure or a recording-quality failure rather than silently
  dropping speech.
- `recording RpcStream`: a non-realtime upload stream. It may temporarily hold
  unsent frames, but ACK handling must not compact or skip recorded audio.
- `playback RpcStream`: a realtime presentation stream. It may compact unsent
  frames after ACK processing when the receiver has fallen behind. For audio,
  every frame is a valid playback compaction target.
- `drop oldest`: a bounded replay/storage policy. When the storage exceeds its
  size, the oldest frames are removed first.

Only `audio buffer` is allowed to intentionally hold playback latency. Only the
playback side is allowed to intentionally skip audio frames.

## Big Parts

The pipeline has these conceptual stages:

1. `raw audio source`
2. `raw audio processors`
3. `audio encoder`
4. `audio sender`
5. `server audio receiver`
6. `server stream store`
7. `server audio sender`
8. `audio receiver`
9. `audio buffer`
10. `audio decoder`
11. `audio renderer`
12. `audio presentation`

There is no control plane stage.

## Constants

All buffering and stream-credit values should be derived from one shared policy.
The target receive buffer is one tenth of a second, or 5 frames at 50 fps.

```csharp
public static partial class Constants
{
    public static class Audio
    {
        public const int FrameRate = 50;
        public static readonly TimeSpan FrameDuration =
            TimeSpan.FromSeconds(1d / FrameRate); // 20 ms

        public const int TargetBufferSize = 5;
        public static readonly TimeSpan TargetBufferDuration =
            TimeSpan.FromSeconds((double)TargetBufferSize / FrameRate); // 100 ms

        public const int BufferHysteresisSize = 3;
        public const int MinBufferSize =
            TargetBufferSize - BufferHysteresisSize; // 2
        public const int MaxBufferSize =
            TargetBufferSize + BufferHysteresisSize; // 8

        public const int PlaybackRpcStreamBufferSize = 10; // 200 ms
        public const int PlaybackRpcStreamAckPeriod = 5;   // 100 ms

        // Recording upload is not realtime: it should drain quickly, but it
        // must not compact or skip speech frames.
        public const int RecordingRpcStreamAckPeriod = 5; // 100 ms

        public const int VoiceStartPreRollSize = 10; // 200 ms

        public static readonly TimeSpan PlaybackHardSkipThreshold =
            TimeSpan.FromSeconds(2);
        public static readonly TimeSpan PlaybackMaxSpeedUpDuration =
            TimeSpan.FromSeconds(5);
        public const int PlaybackSpeedUpDropEveryNFrames = 4;
    }
}
```

The audio target buffer is roughly one third of the video target buffer
because audio has no decoder warmup, no keyframe wait, and no quality
switching to absorb. When audio is paired with video from the same author,
the audio buffer extends to match the video target delay (see Time Model).

The exact playback catch-up thresholds above are policy placeholders. The
important part is the decision shape: small misalignment can be corrected by
temporary frame dropping/speed-up; large misalignment should hard-skip to the
desired presentation point.

## Ownership Model

The `audio buffer` is the only intentional live-audio buffer. Every other
buffer exists only to absorb short fluctuations between adjacent pipeline
components. Intermediate buffers should normally be empty or nearly empty.
On the recording/upload path, intermediate buffers must not intentionally drop
recorded speech. On the playback path, if a component must drop encoded frames,
it drops the oldest frame and resumes immediately; there is no decoder-safety
constraint to respect.

The `raw audio processors` may keep one bounded pre-roll buffer at voice
start; that is a feature of voice activity segmentation, not playback
latency.

## `raw audio source`

The `raw audio source` produces uncompressed PCM samples from a microphone
and immediately passes them to the next component.

**Buffering:** None.

The platform capture API typically delivers samples in fixed-size blocks.
The source emits each block as soon as it arrives. If the platform itself
holds a capture queue (driver buffers, system audio framework), the source
is responsible only for draining that queue; it does not add a queue of its
own.

## `raw audio processors`

The `raw audio processors` transform uncompressed samples before encoding,
such as echo cancellation, noise suppression, automatic gain control,
resampling, and voice activity detection. They also assemble PCM into
encoder-sized frames.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Per-stage handoff | `lossless handoff` | short queue, drain immediately |
| Voice-start pre-roll | `drop oldest` | bounded recent history |
| Pre-roll size | `VoiceStartPreRollSize` | 10 frames (200 ms) |

Most processors operate on a single sample block at a time and pass the
result forward immediately. They do not queue.

The voice-start pre-roll is the one intentional buffer at this stage. While
voice activity is not detected, the pre-roll buffer holds the most recent
200 ms of processed PCM and continually discards older samples. When voice
activity is detected, the pre-roll is fed into the encoder so the leading
consonants of speech are not cut off. Pre-roll trimming must be conservative:
it may discard non-speech context before the recording segment, but it must not
discard audio that should be transcribed.

The pre-roll is not a playback buffer; it adds no latency to audio that is
already being sent.

## `audio encoder`

The `audio encoder` converts PCM frames into encoded audio frames.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `lossless handoff` | short queue, no intentional speech drop |

Audio encoding is fast relative to the frame duration, so this handoff should
normally stay empty. If encoding is somehow slower than capture, the encoder
should apply backpressure where possible or report a recording-quality failure.
It should not hide sustained overload by building a long queue, and it should
not silently drop speech frames.

There is no encoder-side adaptation: bitrate, sample rate, and codec are
chosen at stream creation and remain fixed for the stream's lifetime.

## `audio sender`

The `audio sender` streams encoded frames from the producing client to the
server.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `recording RpcStream` | non-realtime upload |
| `IsRealTime` | constant | `false` |
| `CanSkipTo` | predicate | none |
| `AckPeriod` | `RecordingRpcStreamAckPeriod` | 5 frames |

The `audio sender` should stream encoded frames to the server as quickly as
possible, but it must not use realtime compaction or sender-side skip. The
server uses this stream for transcription and persistence, so missing frames
are data loss, not latency correction.

Small upload queues are acceptable as transport fluctuation absorbers. They
should be drained aggressively and should not become intentional latency. If
the sender cannot keep up, the failure should be visible rather than converted
into silent speech deletion.

## `server audio receiver`

The `server audio receiver` accepts encoded frames from the producing
client, validates them, and forwards them into server-side stream storage.

**Buffering:** None.

The inbound recording stream should not skip. If the server observes missing
or discontinuous recorded audio, that is a recording-quality/transcription
problem rather than normal realtime behavior.

## `server stream store`

The `server stream store` retains the complete recording stream for the
lifetime of the recording. Transcription and persistence consume the full
stream from the beginning; the store does not impose its own retention
bound while the stream is live.

**Buffering:** Full retention until end-of-stream and expiration.

Live presentation fan-out reads from the same retained stream. There is no
separate "live replay tail" stored on the server: the size of the pre-roll
a live joiner receives is determined by the offset the joiner asks for, not
by a server-side cap. A live joiner should request at most
`TargetBufferDuration` of pre-roll — enough to fill its `audio buffer` once,
and no more — because anything beyond that becomes added playback latency
that the receiver has to either tolerate or skip past.

The store should not maintain per-receiver playback latency on behalf of
live consumers; each consumer's `audio buffer` is its own responsibility.

## `server audio sender`

The `server audio sender` fans encoded frames out to receivers.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `playback RpcStream` | real-time |
| `IsRealTime` | constant | `true` |
| `CanSkipTo` | predicate | always `true` |
| `BufferSize` | `PlaybackRpcStreamBufferSize` | 10 frames |
| `AckPeriod` | `PlaybackRpcStreamAckPeriod` | 5 frames |

The `server audio sender` may compact unsent playback backlog after ACKs
because this stream is for presentation, not recording upload. There is no
per-receiver layer selection because audio has no simulcast layers.

If a stream is muted at the server (for example a receiver-side mute toggle
that suppresses delivery), the `server audio sender` should stop yielding
frames rather than send-and-discard, so receive bandwidth is actually
reclaimed.

## `audio receiver`

The `audio receiver` consumes the server stream and drains encoded frames
into the `audio buffer`.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Transport receive buffer | stream implementation detail | drain immediately |

The `audio receiver` should not contain a second independent playback
buffer. If reconnect, realtime stream compaction, or video-driven sync skips
forward, the `audio receiver` resumes from the next frame; no decoder-safety
wait is needed.

## `audio buffer`

The `audio buffer` stores encoded frames before decode and owns the target
receive buffer duration. Frame counts are policy equivalents at the nominal
frame rate; health is measured by buffered media duration.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | intentional playback buffer | drop oldest on overflow |
| Size | `TargetBufferSize` | 5 frames |
| Duration | `TargetBufferDuration` | 100 ms |
| Minimum healthy size | `MinBufferSize` | 2 frames |
| Maximum healthy size | `MaxBufferSize` | 8 frames |
| Hysteresis | `BufferHysteresisSize` | 3 frames |

If the `audio buffer` grows above the maximum healthy duration, it should
drop the oldest frames until it returns to the target duration. Because
every audio frame is a valid resume point, there is no skip-to-keyframe
constraint. If the `audio buffer` stays below the minimum healthy duration,
the receiver is starving.

When the `audio buffer` underruns, the `audio renderer` emits silence until
buffered duration reaches the target again. Repeated underruns may cause the
buffer to grow its effective target (a self-tuning safety margin), but this
should be a slow, hysteretic adjustment so a single network glitch does not
permanently raise latency.

When audio is paired with video from the same author, the `audio buffer`
adopts the video target delay instead of `TargetBufferDuration` (see Time
Model). If the buffer is behind the desired video-aligned presentation point,
it catches up by either temporary speed-up frame dropping or a hard skip.

## `audio decoder`

The `audio decoder` consumes encoded frames from the `audio buffer` and
produces decoded PCM frames for the `audio renderer`.

**Buffering:** None.

The `audio decoder` should decode frames from the `audio buffer` and
immediately hand each decoded PCM frame to the `audio renderer`. Opus
decoding is fast; if decode work accumulates, the receiver is decode-bound
and should report that condition through whatever observability layer the
client uses.

A small encoder-side preamble (Opus pre-skip samples) may be discarded at
the start of a stream for decoder correctness; that is a one-time
decoder-state action, not a buffering policy.

## `audio renderer`

The `audio renderer` accepts decoded PCM and submits it to the platform
audio output mechanism (typically an audio worklet or system audio queue).

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | platform output handoff | just-in-time fill |

The platform audio output pulls samples at a fixed rate dictated by the
audio clock. The `audio renderer` keeps the platform's output queue filled
just enough to avoid underrun between successive `audio buffer` pulls. This
is a thin handoff, not an intentional buffer; any sustained accumulation
here is a bug.

When the `audio buffer` cannot supply samples in time, the `audio renderer`
emits silence to keep the audio clock running. Silence is preferable to a
discontinuity because it preserves sample-accurate timing for everything
that follows.

## `audio presentation`

The `audio presentation` is the audible result: the platform's audio output
playing through speakers or headphones.

**Buffering:** None.

The platform clock owns presentation timing. The `audio renderer` does not
rate-shift, resample, or otherwise alter the playback rate for drift
correction; rate changes would shift pitch and are audible. Drift is corrected
upstream by skipping frames in the `audio buffer` (when far behind), by
temporarily dropping a regular pattern of frames to speed up playback (when
slightly behind), or by emitting silence in the `audio renderer` (when ahead
or starving).

## Time Model

Every audio and video unit should carry origin capture time. The receiver
uses that origin timeline to build a local presentation mapping for each
author or media stream:

```text
origin media time -> local presentation time
```

For audio-only streams, the `audio buffer` establishes the target
presentation delay. The mapping is set once at stream start so that the
first frame plays roughly `TargetBufferDuration` after it arrived.
Subsequent corrections happen by adjusting the target buffer fill, never by
shifting the mapping mid-playback.

For audio paired with video from the same author, the audio target delay
matches the video target delay so audio and video stay synchronized. In this
case the `audio buffer` may be deeper than `TargetBufferDuration` and the
video pipeline establishes the shared delay because video is realtime and owns
the A/V presentation point. Audio adopts that delay; it does not drive it.

This matters when audio delivery and video delivery have different transport
semantics. For example, a receiver may accumulate 30 seconds of audio from an
author while video was unavailable or offline, then begin receiving that
author's video in realtime. Once video establishes the current presentation
point, the audio buffer may discover that much of its queued audio is too old
to present. The receiver must then catch audio up to the video-aligned point.

The catch-up decision has two options:

1. **Temporary speed-up:** drop a regular pattern of audio frames, such as
   every fourth frame, so audio plays faster while remaining intelligible.
2. **Hard skip:** discard the old audio region and resume at the desired
   presentation point.

The receiver chooses between these by estimating how long speed-up would take.
If the needed correction can be absorbed within a short configured window,
for example 3-5 seconds, it uses temporary speed-up. If the correction is too
large, for example at or above roughly 2 seconds of skipped media or whatever
policy threshold is chosen, it hard-skips. The exact thresholds are policy
values; the important invariant is that playback sync decisions happen on the
receiving/presentation side, not on the recording upload side.

If the same author publishes audio and video as independent streams, the
receiver pairs them by author identity and origin timeline. Pairing is
established when enough timing information is available. Once paired, the
audio buffer follows the video presentation point but does not chase short
fluctuations.

## Stream Lifecycle

Live audio is segmented by voice activity:

- A new stream begins when the sender's voice activity detector fires.
  Pre-roll from the `raw audio processors` is encoded and prepended so the
  leading consonants survive.
- A stream ends when sustained silence is detected. The sender flushes all
  trailing encoded frames and completes the RPC stream.
- A user's continuous speech may produce multiple discrete streams if voice
  activity briefly drops and resumes.

Each stream is independent. There is no continuity expectation between
consecutive streams from the same author; the receiver treats each as a
new playback session with its own `audio buffer` fill phase. The receiver
may overlap the tail of one stream with the head of the next when their
origin timestamps abut, so the listener does not hear a gap.

Stream completion is authoritative: once a stream ends, the receiver should
play out remaining buffered audio and then stop, not wait for more frames.

## Startup

When a receiver joins a live audio stream, the receiver may request a short
recent tail from the `server stream store` to pre-fill the `audio buffer`.
The requested tail should not exceed `TargetBufferDuration`; anything more
becomes added latency the receiver immediately has to skip past.

Initial fill should reach `TargetBufferSize` before playback starts.
Because Opus has no decoder warmup beyond its pre-skip samples, playback
can begin as soon as the buffer fills; there is no equivalent of a video
keyframe wait.

If the joining receiver is also receiving video from the same author, the
audio side should defer playback start until the shared A/V target delay
is reached, even if the audio buffer alone is ready sooner. This avoids a
brief audio-leads-video moment at join time.

If video appears after audio has already accumulated, startup is treated as a
sync correction: the receiver either speeds up audio by dropping a regular
frame pattern for a short time, or hard-skips to the video-aligned desired
audio position when the correction is too large.
