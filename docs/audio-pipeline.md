# Audio Pipeline

This document describes the target high-level design of the live audio pipeline.
It is intentionally conceptual: it names the big parts of the pipeline and the
buffering responsibilities between them, without tying the design to current
files, classes, or implementation details.

Audio differs from video in three ways that shape this design:

- Every audio frame is independently decodable. There is no keyframe
  dependency, so any frame is a valid skip target.
- Audio frame size is small and the frame rate is high (a 20 ms Opus frame is
  on the order of 80 bytes), so encoded audio bandwidth is not a constraint
  worth adapting to.
- Audio output is sample-rate locked. Once playback starts, the platform
  audio clock advances at a fixed rate; corrections happen by skipping or
  extending the buffer, not by changing playback rate.

Because of these properties, audio has **no control plane**. There is no
runtime adaptation of bitrate, codec, or layer; quality choice happens once
at stream creation and does not change. The pipeline design optimizes purely
for low, predictable latency.

## Goals

- Keep live audio latency low, predictable, and bounded.
- Make `audio buffer` responsible for intentional buffering.
- Minimize latency everywhere else in the pipeline.
- Treat intermediate queues as short-lived fluctuation absorbers, not playback
  latency.
- Skip audio at any frame; audio has no decoder-state dependency, so any
  frame is a valid resume point.
- Prefer real-time stream semantics over ad-hoc queue overflow policies.
- Keep audio aligned with video presentation through a shared origin
  timeline.

## Buffering Concepts

The pipeline distinguishes three concepts:

- `replaceable slot`: a size-1 handoff slot before a slow component. It does not
  intentionally add latency. It prevents the slow component from going idle
  while waiting for the next frame. If a newer frame arrives while the slot is
  occupied, the newer frame replaces the pending frame.
- `RpcStream`: a real-time stream credit window. It may temporarily hold unsent
  frames, but it is not a playback buffer. After ACK processing, real-time stream
  behavior compacts unsent frames to the latest frame when possible. For audio,
  every frame is a valid compaction target.
- `drop oldest`: a bounded replay/storage policy. When the storage exceeds its
  size, the oldest frames are removed first.

Only `audio buffer` is allowed to intentionally hold playback latency.

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

        public const int RpcStreamBufferSize = 10; // 200 ms
        public const int RpcStreamAckPeriod = 5;   // 100 ms

        public static readonly TimeSpan ServerReplayTailDuration =
            TimeSpan.FromMilliseconds(200);
        public const int ServerReplayTailSize = 10;

        public const int VoiceStartPreRollSize = 10; // 200 ms
    }
}
```

The audio target buffer is roughly one third of the video target buffer
because audio has no decoder warmup, no keyframe wait, and no quality
switching to absorb. When audio is paired with video from the same author,
the audio buffer extends to match the video target delay (see Time Model).

## Ownership Model

The `audio buffer` is the only intentional live-audio buffer. Every other
buffer exists only to absorb short fluctuations between adjacent pipeline
components. Intermediate buffers should normally be empty or nearly empty.
If an intermediate component must drop encoded frames, it drops the oldest
frame and resumes immediately; there is no decoder-safety constraint to
respect.

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
| Per-stage handoff | `replaceable slot` | latest sample block replaces pending block |
| Voice-start pre-roll | `drop oldest` | bounded recent history |
| Pre-roll size | `VoiceStartPreRollSize` | 10 frames (200 ms) |

Most processors operate on a single sample block at a time and pass the
result forward immediately. They do not queue.

The voice-start pre-roll is the one intentional buffer at this stage. While
voice activity is not detected, the pre-roll buffer holds the most recent
200 ms of processed PCM and continually discards older samples. When voice
activity is detected, the pre-roll is fed into the encoder so the leading
consonants of speech are not cut off. Pre-roll trimming based on signal
gain may discard part of this buffer at the moment voice starts.

The pre-roll is not a playback buffer; it adds no latency to audio that is
already being sent.

## `audio encoder`

The `audio encoder` converts PCM frames into encoded audio frames.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `replaceable slot` | latest frame replaces pending frame |

Audio encoding is fast relative to the frame duration, so this slot should
normally stay empty. If encoding is somehow slower than capture, a newer
PCM frame replaces the pending one. The `audio encoder` should not hide
sustained overload by building a queue.

There is no encoder-side adaptation: bitrate, sample rate, and codec are
chosen at stream creation and remain fixed for the stream's lifetime.

## `audio sender`

The `audio sender` streams encoded frames from the producing client to the
server.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `RpcStream` | real-time |
| `IsRealTime` | constant | `true` |
| `CanSkipTo` | predicate | always `true` |
| `BufferSize` | `RpcStreamBufferSize` | 10 frames |
| `AckPeriod` | `RpcStreamAckPeriod` | 5 frames |

Because every audio frame is a valid skip target, ACK compaction is
unrestricted: if the `audio sender` has an unsent tail, it drops everything
before the latest unsent frame and then sends normally. This is the only
sender-side drop policy; there is no separate producer-side queue layered on
top of the RPC stream.

## `server audio receiver`

The `server audio receiver` accepts encoded frames from the producing
client, validates them, and forwards them into server-side stream storage.

**Buffering:** None.

If the inbound stream skipped, the receiver simply continues from the next
arriving frame. There is no recovery handshake.

## `server stream store`

The `server stream store` keeps a short live replay window for late join,
reconnect, and fan-out.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `drop oldest` | bounded tail |
| Size | `ServerReplayTailSize` | 10 frames |
| Duration | `ServerReplayTailDuration` | 200 ms |

The `server stream store` is not the main playback buffer. A receiver may
read recent frames to populate its own `audio buffer`, but the server should
not maintain per-receiver playback latency inside the store. The replay tail
should not exceed the receiver's target buffer duration, because anything
beyond that becomes additional playback latency that the receiver has to
either tolerate or skip past.

## `server audio sender`

The `server audio sender` fans encoded frames out to receivers.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `RpcStream` | real-time |
| `IsRealTime` | constant | `true` |
| `CanSkipTo` | predicate | always `true` |
| `BufferSize` | `RpcStreamBufferSize` | 10 frames |
| `AckPeriod` | `RpcStreamAckPeriod` | 5 frames |

The `server audio sender` should compact unsent backlog after ACKs in the
same way as the `audio sender`. There is no per-receiver layer selection
because audio has no simulcast layers.

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
buffer. If reconnect or real-time stream compaction skips forward, the
`audio receiver` resumes from the next frame; no decoder-safety wait is
needed.

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
Model).

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
correction; rate changes would shift pitch and are audible. Drift is
corrected upstream by skipping forward in the `audio buffer` (when behind)
or by emitting silence in the `audio renderer` (when ahead or starving).

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
matches the video target delay so audio and video stay synchronized. In
this case the `audio buffer` may be deeper than `TargetBufferDuration` and
the video pipeline establishes the shared delay because video has the
explicit playback buffer that owns A/V timing. Audio adopts that delay; it
does not drive it.

If the same author publishes audio and video as independent streams, the
receiver pairs them by author identity and origin timeline. Pairing is
established at stream start; once paired, the audio buffer extends and the
mapping does not chase short fluctuations.

## Stream Lifecycle

Live audio is segmented by voice activity:

- A new stream begins when the sender's voice activity detector fires.
  Pre-roll from the `raw audio processors` is encoded and prepended so the
  leading consonants survive.
- A stream ends when sustained silence is detected. The sender flushes any
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
