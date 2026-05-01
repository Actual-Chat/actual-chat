# Video Pipeline

This document describes the target high-level design of the live video pipeline.
It is intentionally conceptual: it names the pipeline components and the
buffering responsibilities between them, without tying the design to current
files, classes, or implementation details.

## Goals

- Keep live video latency understandable and bounded.
- Make `video buffer` responsible for intentional buffering.
- Minimize latency everywhere else in the pipeline.
- Treat intermediate queues as short-lived fluctuation absorbers, not playback
  latency.
- Skip encoded video only at decoder-safe points.
- Prefer real-time stream semantics over ad-hoc queue overflow policies.
- Make quality adaptation react to stable receiver signals rather than local
  queue accidents.

## Buffering Concepts

The pipeline distinguishes three concepts:

- `replaceable slot`: a size-1 handoff slot before a slow component. It does not
  intentionally add latency. It prevents the slow component from going idle
  while waiting for the next frame. If a newer frame arrives while the slot is
  occupied, the newer frame replaces the pending frame.
- `RpcStream`: a real-time stream credit window. It may temporarily hold unsent
  frames, but it is not a playback buffer. After ACK processing, real-time stream
  behavior compacts unsent frames to the latest decoder-safe frame when possible.
- `drop oldest`: a bounded replay/storage policy. When the storage exceeds its
  size, the oldest frames are removed first.

Only `video buffer` is allowed to intentionally hold playback latency.

## Pipeline Components

The pipeline has these conceptual stages:

1. `raw video source`
2. `raw video processors`
3. `video encoder`
4. `video sender`
5. `server video receiver`
6. `server stream store`
7. `server video sender`
8. `video receiver`
9. `video buffer`
10. `video decoder`
11. `video renderer`
12. `video presentation`
13. `control plane`

## Buffering and Skipping Points

Every place in the pipeline that intentionally holds a frame, drops a frame, or
compacts unsent frames:

| Where | Policy | What it does |
|---|---|---|
| `raw video processors` | `replaceable slot` | Newer raw frame replaces a pending one if the processors are still working on the previous frame. |
| `video encoder` | `replaceable slot` | Newer raw frame replaces a pending one if the encoder is still working on the previous frame. |
| `video sender` | `RpcStream` (real-time) | Sends encoded frames to the server; on ACK, compacts unsent backlog by skipping forward to the latest decoder-safe frame. |
| `server stream store` | `drop oldest keyframe span` | Bounded short replay tail for late join; evicts oldest keyframe-anchored span when full. |
| `server video sender` | `RpcStream` (real-time) | Fans frames out to a receiver; same ACK-compaction-to-keyframe semantics as the client sender. |
| `video buffer` | intentional playback buffer | The only intentional playback latency; trims to a keyframe when above maximum healthy duration; signals starvation when below minimum. |
| `video presentation` | `replaceable slot` | Newer decoded frame replaces a pending one while the presentation step is waiting for the next screen refresh. |

Other stages — `raw video source`, `server video receiver`, `video receiver`,
`video decoder`, `video renderer` — should not buffer or drop frames; they
forward each frame as soon as it arrives.

## Constants

All buffering and stream-credit values should be derived from one shared policy.
The target receive buffer is one third of a second, or 10 frames at 30 fps.

```csharp
public static partial class Constants
{
    public static class Video
    {
        public const int FrameRate = 30;
        public static readonly TimeSpan FrameDuration =
            TimeSpan.FromSeconds(1d / FrameRate); // 33.333 ms

        public const int TargetBufferSize = 10;
        public static readonly TimeSpan TargetBufferDuration =
            TimeSpan.FromSeconds((double)TargetBufferSize / FrameRate); // 333.333 ms

        public static readonly TimeSpan KeyFramePeriod = TimeSpan.FromSeconds(3);
        public const int KeyFramePeriodSize = FrameRate * 3; // 90

        public const int BufferHysteresisSize = TargetBufferSize / 2; // 5
        public const int MinBufferSize =
            TargetBufferSize - BufferHysteresisSize; // 5
        public const int MaxBufferSize =
            TargetBufferSize + BufferHysteresisSize; // 15

        public const int RpcStreamBufferSize = TargetBufferSize; // 10
        public const int RpcStreamAckPeriod = TargetBufferSize / 2; // 5

        public static readonly TimeSpan ServerReplayTailDuration = TimeSpan.FromSeconds(1);
        public const int ServerReplayTailSize = FrameRate; // 30
    }
}
```

## Ownership Model

The `video buffer` is the only intentional live-video buffer. Every other buffer
exists only to absorb short fluctuations between adjacent pipeline components.
Intermediate buffers should normally be empty or nearly empty. If an
intermediate component must drop encoded frames, it must only resume from a
decoder-safe frame.

## `raw video source`

The `raw video source` produces uncompressed video frames from a camera or
screen source and immediately passes them to the next component.

**Buffering:** None.

## `raw video processors`

The `raw video processors` transform uncompressed frames before encoding, such
as blur, orientation correction, scaling, cropping, color conversion, or visual
effects.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `replaceable slot` | latest frame replaces pending frame |

The `raw video processors` use this single frame as a replaceable next-frame
slot. If processing is slower than capture, a newer raw frame replaces the
pending frame. This lets the processors run at their real throughput without
accumulating latency.

## `video encoder`

The `video encoder` converts raw frames into encoded video frames and may produce
multiple simulcast spatial layers for the same source timeline.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `replaceable slot` | latest frame replaces pending frame |

The `video encoder` uses this single frame as a replaceable next-frame slot. If
encoding is slower than raw-frame production, a newer raw frame replaces the
pending frame. The `video encoder` should not hide sustained overload by
building a queue.

## `video sender`

The `video sender` streams encoded frames from the producing client to the
server.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `RpcStream` | real-time |
| `IsRealTime` | constant | `true` |
| `CanSkipTo` | decoder-safe frame predicate | keyframe |
| `BufferSize` | `RpcStreamBufferSize` | 10 frames |
| `AckPeriod` | `RpcStreamAckPeriod` | 5 frames |

ACK compaction happens only after ACK processing. If the `video sender` has an
unsent tail containing one or more decoder-safe frames, it drops everything
before the latest decoder-safe frame in that unsent tail and then sends normally.

## `server video receiver`

The `server video receiver` accepts encoded frames from the producing client,
validates and classifies them, and forwards them into server-side stream storage.

**Buffering:** None.

If the inbound stream skips, the skip should have already happened at a
decoder-safe point before frames reach the `server video receiver`.

## `server stream store`

The `server stream store` keeps a short live replay window for late join,
reconnect, and fan-out.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `drop oldest keyframe span` | bounded decodable replay |
| Size | `ServerReplayTailSize` | 30 frames |
| Duration | `ServerReplayTailDuration` | 1 s |

The `server stream store` is not the main playback buffer. A receiver may read
recent frames to populate its own `video buffer`, but the server should not
maintain per-receiver playback latency inside the store. The replay tail does
not need to guarantee that a keyframe is present; if the recent tail does not
contain a decoder-safe frame, the receiver should wait for or request the next
one.

Because the replay tail can be shorter than the keyframe period, the `server
stream store` should only retain decodable keyframe-anchored spans. Delta frames
whose anchor keyframe has fallen out of the replay tail are not useful replay
data and should be removed together with that keyframe span.

## `server video sender`

The `server video sender` fans encoded frames out to receivers and may select an
appropriate simulcast layer per receiver.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `RpcStream` | real-time |
| `IsRealTime` | constant | `true` |
| `CanSkipTo` | decoder-safe frame predicate | keyframe |
| `BufferSize` | `RpcStreamBufferSize` | 10 frames |
| `AckPeriod` | `RpcStreamAckPeriod` | 5 frames |

The `server video sender` should compact unsent backlog after ACKs in the same
way as the `video sender`. Once a simulcast layer is selected for a receiver,
the `server video sender` should avoid mixing incompatible encoded streams.

## `video receiver`

The `video receiver` consumes the server stream and drains encoded frames into
the `video buffer`.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Transport receive buffer | stream implementation detail | drain immediately |

The `video receiver` should not contain a second independent playback buffer. If
reconnect or real-time stream compaction skips forward, the `video receiver`
must resume only from decoder-safe frames.

## `video buffer`

The `video buffer` stores encoded frames before decode and owns the target
receive buffer duration. Frame counts are policy equivalents at the nominal
frame rate; health is measured by buffered media time because live video is
effectively variable frame rate.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | intentional playback buffer | keyframe-aware trim |
| Size | `TargetBufferSize` | 10 frames |
| Duration | `TargetBufferDuration` | 333.333 ms |
| Minimum healthy size | `MinBufferSize` | 5 frames |
| Maximum healthy size | `MaxBufferSize` | 15 frames |
| Hysteresis | `BufferHysteresisSize` | 5 frames |

If the `video buffer` grows above the maximum healthy duration, it should skip
forward to a suitable keyframe and discard older encoded frames. If the `video
buffer` stays below the minimum healthy duration, that is a receiver health
signal.

## `video decoder`

The `video decoder` consumes encoded frames from the `video buffer` and produces
decoded frames for the `video renderer`.

**Buffering:** None.

The `video decoder` should decode frames from the `video buffer` and immediately
hand each decoded frame to the `video renderer`. If decode work accumulates, the
receiver is decode-bound and should report that condition through the `control
plane`.

## `video renderer`

The `video renderer` accepts decoded frames and draws or submits them to the
platform video output mechanism.

**Buffering:** None.

The `video renderer` should be thin and should not own presentation timing. It
draws or submits the frame selected by `video presentation`.

## `video presentation`

The `video presentation` is the visible playback result: timing, media element
state, layout, and audio/video synchronization.

**Buffering:**

| Parameter | Formula | Actual value |
|---|---:|---:|
| Policy | `replaceable slot` | latest frame replaces pending frame |

The `video presentation` may wait for screen refresh before presenting a decoded
frame. If a newer decoded frame arrives while `video presentation` is waiting,
the newer frame replaces the pending frame. This is a replaceable slot, not a
queue. Once a frame is decoded, it is too late to make keyframe-aware
encoded-frame skip decisions.

## `control plane`

The `control plane` carries coarse health and quality intent. It should not
move individual frames and should not own media buffering. Its job is to let
clients and the server converge on a sustainable send and receive quality while
the media pipeline keeps frame flow simple.

### Time Model

Every audio and video unit should carry origin capture time. The receiver uses
that origin timeline to build a local presentation mapping for each author or
media stream:

```text
origin media time -> local presentation time
```

Video establishes the target presentation delay because it has the explicit
`video buffer`. Audio from the same author should target the same delay so that
audio and video stay synchronized. The delay should adjust slowly; large video
corrections happen by keyframe-aware skips in the `video buffer`, while normal
playback timing should avoid chasing short fluctuations.

Server ingress time is useful telemetry. It helps estimate sender-to-server
delay and diagnose routing or region effects, but A/V sync should rely on the
shared origin media timeline.

### Startup

Startup should request a recent media duration, not a fixed frame count. The
receiver may ask the `server stream store` for a short playable tail before the
live edge and then continue with live frames. This tail is used only to fill the
local `video buffer`; it is not a second playback buffer.

The startup tail must still be decoder-safe. If the available recent tail does
not contain a keyframe-anchored span, playback should wait for the next
decoder-safe point rather than starting from orphan delta frames.

### Receiver Signals

Each client reports one coarse receive state over a rolling window:

| State | Meaning | Server response |
|---|---|---|
| `starving` | The client cannot keep the target media duration in one or more active `video buffer` instances. | Reduce the client's receive budget. |
| `healthy` | The client keeps the target duration without repeated skips or underruns. | Keep the current receive budget. |
| `can do more` | The client stays healthy with room to receive more data. | Increase the client's receive budget. |

During initial fill, the client is effectively in a starting state. The server
should treat missing or not-yet-stable feedback conservatively.

The client should also report measured byte rates for the streams it receives.
Those rates let the server translate a coarse state into a concrete per-client
receive budget and into per-stream quality choices.

Useful receiver-side measurements are:

| Measurement | Purpose |
|---|---|
| Buffered media duration per stream | Primary health signal. |
| Starvation or underrun time | Evidence that the current receive budget is too high. |
| Keyframe skips | Evidence that playback had excess latency or had to recover. |
| Decode or presentation overload | Evidence that local hardware, not network, is the bottleneck. |
| Average received byte rate per stream | Input for server-side budgeting. |
| Stream visibility and speaker priority | Input for distributing the receive budget. |

### Receive Budgeting

The server maintains a single receive budget per client. Until client feedback
is available, the server should start conservatively, for example with medium
or slightly below-medium quality for each received video.

When feedback arrives, the server adjusts the total expected byte rate for that
client in coarse steps. A reasonable starting rule is:

| Client state | Budget change |
|---|---:|
| `starving` | decrease by about 20% |
| `healthy` | keep unchanged |
| `can do more` | increase by about 20% |

The server then distributes that budget across the client's received videos.
The primary speaker should get the best quality that fits the budget. The
remaining visible videos should share the rest of the budget in a roughly equal
way, adjusted by availability and any UI priority.

The server should think in measured byte rates, not just named quality levels.
Named qualities are only choices available to satisfy a byte budget.

### Stream Quality Switching

Each client should receive one logical video stream for each remote video. The
preferred behavior is for the server to change the encoded quality inside that
logical stream at decoder-safe boundaries, rather than making the client switch
between separately visible streams.

Quality changes should happen at keyframes. A keyframe and the following delta
frames should belong to the same encoded layer, resolution, and bitrate profile
until the next quality switch. This avoids mixing incompatible delta chains and
reduces visible flashing during adaptation.

The server-side stream storage may keep all produced simulcast layers. The
`server video sender` chooses which layer to forward for each receiver according
to that receiver's current budget and stream priorities.

### Sender Signals

The sending side has priority because it consumes the client's outgoing
capacity and local encoder resources. Sending quality can be limited by two
separate constraints:

| Constraint | Signal | Local response |
|---|---|---|
| Outgoing byte rate | Sender stream backlog, ACK lag, or sustained send pressure. | Drop the highest simulcast layer or reduce produced bitrate. |
| Hardware encode cost | Encode and processing time too close to or above frame duration. | Drop the highest simulcast layer or reduce resolution. |

If the sender is already at the minimum reasonable resolution, for example
360p, continued overload should show up as lower effective frame rate rather
than hidden queue growth.

### Decision Cadence

Signals should be aggregated over a window, for example three to five seconds.
Downgrades may happen quickly after repeated bad signal. Upgrades should require
sustained healthy signal and should use cooldowns so quality does not oscillate.

The separation is intentional:

```text
video buffer controls playback timing
RpcStream controls decoder-safe catch-up
control plane controls future quality
```
