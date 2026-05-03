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

## Recording Quality Control

The recording side regulates its own outgoing quality on the client, with no
feedback loop from the server. Outgoing capacity and local encoder cost are
properties of the sending client alone — independent of any receiver's
playback budget — so the sender's quality decision is purely local. The
server is told the current state for metrics but does not act on it.

This is a peer to `control plane` below: the control plane handles
receive-side budget convergence; recording quality control handles send-side
adaptation. Splitting them lets each loop run at its own cadence without
fighting the other.

### Goals

- Keep the recording stream within the client's outgoing capacity and
  encoder budget without artificial buffering.
- Make the quality decision on the client, fast and local, so it does not
  fight or oscillate with receive-side adaptation that runs independently
  per remote viewer.
- Surface the decision and its inputs to the server as metrics only.

### Action Surface

The recording controller adjusts a single integer:

| Knob | Type | Range | Initial |
|---|---|---:|---:|
| `targetLayerCount` | int | 1–4 | 2 |

Each value maps to a curated simulcast ladder shape. Platform caps clamp the
upper bound (e.g. iOS = 2, mobile = 3, desktop = 4). The encoder produces
exactly that many simulcast layers; the bottom layer is always 360p-class,
each higher layer doubles in pixel area, and the top layer matches the
source resolution at `targetLayerCount = 4`.

The controller is one shared state per `StreamKind` (webcam, screencast),
held on the client across reconnects. Different kinds have different cost
profiles and so each maintains its own `targetLayerCount`.

### Signals

Two families of signal, sampled by the recording worker and the sender's
`RpcStream` and aggregated by the controller in 1-second windows:

| Signal | Source | Healthy band |
|---|---|---|
| `encodeRatio.p90` | encoder + downscaler timing in the recording worker, normalised to frame duration | < 0.5 |
| `slotReplacementRate` | encoder slot replacement count / frames produced | < 1 % |
| `senderBacklog.p90` | oldest-unacked age on the sender's `RpcStream`, p90 over window | < 50 ms |
| `senderSkipsPerWindow` | `RpcStream`'s ACK-driven compaction count | 0 |
| `lastAckAge` | time since the most recent ACK on the sender's `RpcStream` | < 0.5 s |
| `isConnected` | client-wide peer connectivity | true |

The first two cover the encoder/hardware budget. The next three cover
outgoing network capacity. The last gates the entire controller: a
disconnected peer freezes classification.

### Ternary Classifier

Each signal is mapped to a ternary verdict per window:

| Verdict | Meaning |
|---|---|
| `+1` | comfortably inside the healthy band |
| `0` | borderline — neither push down nor count toward push up |
| `-1` | outside the healthy band |

The middle "neutral" band is the design's safeguard against slow climbs
into trouble. A signal that is "OK but not great" parks at the current
state instead of contributing to a step-up.

### Aggregation

Per window:

- **Any signal at `-1`** → step down by one. Cooldown of K windows after
  the change.
- **All signals at `+1`** for K consecutive windows past the last change
  → step up by one.
- **Otherwise** → hold.

K is in the order of 5 seconds at 1 Hz cadence. The asymmetry is in the
entry conditions (any vs. all), not in the cadence — climbing is as slow
as backoff is fast.

### Floor Behavior

`targetLayerCount = 1` is sticky under sustained `-1`. Frame-rate
degradation emerges naturally from the encoder's replaceable slot — the
slot replaces frames whenever the encoder is overloaded, which produces
fewer encoded frames per wall-clock second without any explicit
frame-rate knob. The controller surfaces the floor state to the server
with a "stuck at floor" reason flag so dashboards can detect persistent
bottom-tier streams.

### Reconnect Handling

Connectivity is read from a single client-wide source (not per-stream).
While disconnected the controller is frozen — no classification, no
decisions. On reconnect the last-stable `targetLayerCount` is preserved,
the signal windows are wiped, and a brief cold-start grace skips the next
1–2 windows before classification resumes.

### Server Reporting

The controller pushes its current state and signal snapshot to the server
purely as metrics. The endpoint accepts the report and never returns a
directive to the client:

```text
ChangeRecordingQuality(state?, info?) -> RpcNoWait
```

Both arguments are independent and nullable:

- `state` (`RecordingQualityState`) — current decision: target layer
  count and effective layer count. `null` means "no change".
- `info` (`RecordingQualityInfo`) — reason for the last change plus a
  full signal snapshot. `null` means "no signal payload".

Typical patterns:

| Args | When |
|---|---|
| `(state, info)` | a decision happened; new state + the signals that produced it |
| `(null, info)` | 5 s heartbeat; signals only |
| `(state, null)` | rare — reconnect-time re-assertion before new signals form |

Cadence: a heartbeat every 5 seconds plus an immediate report on every
decision. A decision resets the heartbeat timer.

### Ownership

```text
Active recording streams (per StreamKind)
        ↓
  per-stream signals (1 Hz, recording worker → client)
  RpcStream sender state (ACK events, backlog, skip count)
  client-wide peer connectivity
        ↓
  client-side recording quality controller (1 Hz)
        ├── ternary classify per signal
        ├── aggregate → step down / step up / hold
        └── on decision OR 5 s heartbeat:
             apply state to recorder + report to server (no-wait)
```

The controller is the single writer of `targetLayerCount`. The recording
worker is signal-source-only and never adjusts the ladder on its own.

## Playback Quality Control

The playback side regulates outgoing quality requests for every remote
stream the client is consuming. Like recording quality control, the
decision is made on the client; unlike recording, the decision affects
multiple streams at once and is informed by signals from all of them
together. This avoids cross-stream oscillation that arises when each
remote stream's quality is regulated in isolation.

The server applies what the client asks for. There is no server-side
adaptation loop deriving quality from client-reported signals — the
client's request is authoritative, subject only to a hard safety cap on
how many concurrent streams may exceed the lowest quality.

### Goals

- Match the client's actual incoming-bandwidth capacity to the sum of
  the streams it consumes, with quick adjustment as conditions change.
- Use a single client-wide capacity estimate informed by all active
  streams jointly, so a glitch on one stream does not affect quality on
  another.
- Spend the available capacity by stream priority — the focused stream
  gets the best quality first, secondaries fill the remainder.
- Surface decisions and signals to the server as metrics; let the server
  enforce only a coarse safety cap.

### Action Surface

For each active stream, the client requests one entry:

| Field | Type | Range |
|---|---|---:|
| `maxSpatialLayer` | int | 0–3 |
| `maxTemporalLayer` | int | 0–2 (for L1T3) |

The "lowest quality" is `(0, 0)` — base spatial only, base temporal only.
This replaces the legacy `Paused` enum value: a stream the client cannot
afford to receive at any meaningful quality is asked for at `(0, 0)`,
which produces the smallest possible frame stream short of unsubscribing.

A two-tier priority is implicit in the request:

| Tier | What it means | Default cap |
|---|---|---|
| `Primary` | the focused / full-screen tile | full ladder, all temporal layers |
| `Secondary` | sidebar / off-focus tiles | low spatial, base temporal only |

`Primary` is reserved for the small set of streams the user is actively
watching; everything else is `Secondary`. Secondary streams are always
temporal-capped — frame rate is dropped via temporal layer ID rather
than spatial resolution, so a sidebar tile remains recognisable but
cheaper.

When a stream transitions Secondary → Primary the client pushes a fresh
request immediately, sub-cycle. The server applies on the next keyframe
on the requested layer.

### Signals

Per stream, sampled and aggregated by the controller in 1-second windows:

| Signal | Source | Healthy band |
|---|---|---|
| `bufferDuration` | encoded buffer in the decoder worker | 150–450 ms (target 300) |
| `incomingByteRate` | bytes received in the last window for this stream | weight only — not classified |
| `keyframeSkips` | encoded-buffer keyframe-aware evictions | 0 |
| `decoderQueue.p90` | platform decoder backpressure | < 4 |
| `isConnected` | client-wide peer connectivity | true |

Buffer duration is the primary health signal. Frame counts are not
used — stream rate is variable, so duration is the only stable target.
Incoming byte rate is not classified; it weights each stream's
contribution to the aggregate (see below).

### Per-Stream Ternary Verdict

| Verdict | Meaning |
|---|---|
| `+1` | `bufferDuration > 450 ms` AND no keyframe skips this window |
| `0` | `150 ms ≤ bufferDuration ≤ 450 ms` and no keyframe skips |
| `-1` | `bufferDuration < 150 ms` OR any keyframe skips this window |

### Aggregate Health

A single client-wide score combines per-stream verdicts weighted by
incoming byte rate:

```text
aggregate = Σ(rate_s × verdict_s) / Σ(rate_s)
```

The byte-weighting captures the asymmetric importance of streams: a big
healthy stream and small lagging streams produce `aggregate ≈ 0` (the
small streams' lag is likely a server-side glitch, not bandwidth); a
small healthy stream and a big lagging stream produce `aggregate ≈ -1`
(real congestion).

### Capacity Estimate

The controller maintains a single client-wide `estimatedCapacity` in
bytes per second. Updated per window:

| `aggregate` | Update |
|---|---|
| `> +0.5` | `capacity ← min(capacity × √2, Σ rate_s × √2)` — bounded climb |
| `< -0.5` | `capacity ← capacity × 0.7` — faster than the climb (asymmetric) |
| else | hold |

The `√2` climb cap prevents over-shooting on unrelated bursts (e.g. a
brief keyframe storm on simulcast joins). Backoff is faster than the
climb so the controller errs on the side of staying healthy.

### Allocation

Greedy fill, primary-first:

1. `budget ← estimatedCapacity`
2. For each `Primary` stream, sorted by recent audio activity:
   - Try `(maxSpatial=top, maxTemporal=all)`.
   - Predict cost = `currentRate × bitrate(req) / bitrate(currentLayer)`.
   - Step spatial down until predicted cost ≤ `budget`. Commit; subtract.
   - If at floor, commit at floor.
3. For each `Secondary` stream:
   - Try `(maxSpatial=1, maxTemporal=0)`.
   - Same fit-or-step-down loop on remaining `budget`.
4. Any leftover `budget` → step up `Primary` streams (Secondary stays at
   default).

The default for any new stream is `(maxSpatial=1, maxTemporal=0)` until
the first cycle has signals to make a decision on.

### Cadence

Two cadence modes:

| Mode | Tick | Active when |
|---|---|---|
| Cold start | 2 s | `min(oldStreamCount, newStreamCount) ≤ 3` (small-call transition) |
| Steady | 5 s | otherwise |

Cold start lasts until the active set has been stable for 10 s. Any
change to the active stream set re-enters cold start if the threshold
condition is met. The 5 s heartbeat resets on every decision.

### Floor Behavior

If `aggregate` stays at `-1` and all primaries are already at the lowest
spatial layer, the floor state is sticky — the controller keeps the
request as-is and reports it. No further degradation happens beyond the
already-temporal-capped secondary defaults; if the floor is genuinely
insufficient the user-facing fix is to display fewer streams.

### Reconnect Handling

Connectivity is read from the same client-wide source as Recording
Quality Control. While disconnected the controller is frozen. On
reconnect the last-known per-stream request map is re-pushed
immediately so the server's per-session store, which may have been lost
on the API server side, is re-seeded.

### Server Safety Cap

The server caps how many concurrent streams a client may request at
above-lowest quality:

| Constant | Value | Why |
|---|---:|---|
| `MaxNonLowestStreams` (client rule) | 8 | self-imposed; no `Primary` + `Secondary` count above 8 |
| `ServerCap` (server enforcement) | 9 | one slot of slack so a well-behaved client never trips it |

If the client requests more than `ServerCap` streams above lowest, the
server demotes the surplus to `(0, 0)` (Secondary demoted before
Primary, then by request order). This is abuse prevention only — a
well-behaved client stays strictly below `MaxNonLowestStreams`.

### Server Reporting

The same `ChangePlaybackQuality` call that updates the client's request
also carries a snapshot of decision-making data:

```text
ChangePlaybackQuality(requestedQuality?, info?) -> RpcNoWait
```

- `requestedQuality` (`ApiMap<StreamId, ReceiveQuality>`) — the client's
  per-stream caps. `null` means "no change" (heartbeat).
- `info` (`PlaybackQualityInfo`) — capacity estimate, aggregate health,
  reason for the change, cold-start flag, and per-stream observed
  signals plus currently-served caps. `null` means "no signal payload".

Typical patterns:

| Args | When |
|---|---|
| `(map, info)` | a decision happened; new request map and signals reported |
| `(null, info)` | 5 s heartbeat with no decision change; signals only |
| `(map, null)` | reconnect-time re-assertion of last-known map before any new signals form |

The server stores `requestedQuality` per session. Active stream filters
read from the stored map on each iteration; cap changes apply from the
next keyframe on the affected layer for decoder safety.

### Ownership

```text
Active playback streams (any chat, any tile)
        ↓
  per-stream signals (1 Hz, decoder worker → client)
  client-wide peer connectivity
  current focused/sidebar UI state
        ↓
  client-side playback quality controller (2 s cold-start, 5 s steady)
        ├── per-stream ternary classify
        ├── byte-weighted aggregate → capacity estimate (√2-bounded climb)
        ├── greedy primary-first allocate over `estimatedCapacity`
        └── on decision OR 5 s heartbeat:
             ChangePlaybackQuality(map?, info?) → RpcNoWait
```

The controller is the single writer of the per-session request map. The
server-side filter is read-only relative to the map — it just clamps
and forwards.

## API Surface

The streaming service API is split into per-media services
(`ILiveVideoStreams`, `ILiveAudioStreams`) plus a legacy facade
(`IStreamServer`) kept only for backwards compatibility with old
client builds. New code — both server-side and TypeScript — talks
exclusively to the per-media services.

Sticky routing on the public load balancer keeps every RPC connection
from the same client on the same API server, so per-session state on
either `LiveVideoStreams` or `LiveAudioStreams` is local — no
cross-shard coordination is needed.

### `ILiveVideoStreams`

The authoritative video surface. Every method takes `Session` as the
first argument.

```csharp
public interface ILiveVideoStreams
{
    // Read path
    Task<RpcStream<VideoFrame>?> GetStream(
        Session session, string streamId, TimeSpan skipTo, CancellationToken ct);

    // Write path — moved from IStreamServer
    Task PushVideo(
        Session session, VideoRecord record,
        RpcStream<VideoFrame> stream, CancellationToken ct);
    Task RequestKeyFrame(
        Session session, string streamId, CancellationToken ct);

    // Quality control — client-driven; server applies/records
    Task ChangeRecordingQuality(
        Session session,
        RecordingQualityState? state,
        RecordingQualityInfo? info,
        CancellationToken ct);
    Task ChangePlaybackQuality(
        Session session,
        ApiMap<string /* StreamId */, ReceiveQuality>? requestedQuality,
        PlaybackQualityInfo? info,
        CancellationToken ct);

    // Existing query methods stay (e.g. GetVideoStreamMemberCount).
}
```

Notes:

- `GetStream` is the receiver-facing video read. Server-side calls
  `IVideoStreamingBackend.GetVideoRaw` to obtain the raw unfiltered
  stream and applies a thin layer-cap filter using the per-session
  `ReceiveQuality` map maintained by `ChangePlaybackQuality`. The
  filter does not run a rule engine — only spatial and temporal layer
  caps + skip-until-keyframe on cap changes and on detected gaps.
- `RequestKeyFrame` returns `Task<RpcNoWait>` — fire-and-forget at
  the wire level. The server just sets a PLI flag; the publisher
  picks it up via the existing flag-and-rate-limit logic.
- `ChangeRecordingQuality` is metrics-only on the server; it does not
  affect any forwarded stream — the recorder's quality state is
  enforced locally by the client.
- `ChangePlaybackQuality`'s `info` argument is metrics-only;
  `requestedQuality` is the authoritative per-stream cap map (subject
  to the Server Safety Cap demotion rule).
- `ReportVideoLatency` is intentionally absent — its job is fully
  subsumed by `ChangePlaybackQuality` (per-stream signals) and
  `ChangeRecordingQuality` (sender-side signals).

### `ILiveAudioStreams`

The authoritative audio surface and the new home for transcript reads.
Every method takes `Session` as the first argument.

```csharp
public interface ILiveAudioStreams
{
    // Audio read/write — moved from IStreamServer
    Task PushAudio(
        Session session, AudioRecord record,
        RpcStream<AudioFrame> stream, CancellationToken ct);
    Task<RpcStream<AudioFrame>?> GetStream(
        Session session, string streamId, TimeSpan skipTo, CancellationToken ct);

    // Transcript read — moved from IStreamServer.GetTranscript
    Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
        Session session, string streamId, CancellationToken ct);

    // Audio metrics — stays in current shape; audio quality control
    // is not being redesigned in this pass.
    Task ReportAudioLatency(
        Session session, /* …current shape… */ CancellationToken ct);

    // Existing query methods stay.
}
```

The new audio read method is named `GetStream` (not `GetAudio`) for
symmetry with `ILiveVideoStreams.GetStream`; the service name already
qualifies it.

### `IStreamServer` — legacy

Marked `[Obsolete]` at the type level. Kept solely for backwards
compatibility with old client builds. New TypeScript code never calls
it.

- **Video methods removed entirely** — no proxy, no compat shim:
  `PushVideo`, `GetVideo`, `RequestKeyFrame`, `ReportVideoLatency`.
  Old ActualChat builds cannot push or consume video, so removing
  these is safe.
- **Audio + transcript methods kept as thin proxies** — `PushAudio`,
  `GetAudio`, `GetTranscript` forward to the corresponding
  `ILiveAudioStreams` method, passing `Session.Default` for the
  session parameter. The proxy is a local in-process call; the
  session value is not meaningfully populated, but the implementation
  ignores it today, and the shape is forward-compatible with
  session-aware clients (which arrive there via the WS `?session=`
  URL parameter resolved server-side).

### TypeScript `streaming-api` module

The TS facade module stays and is rebound to surface only the methods
JavaScript actually consumes, organised by service:

| Service | Methods exposed in TS |
|---|---|
| `liveVideoStreams` | `PushVideo`, `GetStream`, `RequestKeyFrame`, `ChangeRecordingQuality`, `ChangePlaybackQuality` |
| `liveAudioStreams` | `PushAudio`, `GetStream`, `GetTranscriptStream`, `ReportAudioLatency` |

JavaScript passes `'~'` (= `RPC_SESSION_DEFAULT`) as the `session`
parameter on every method; the server-side middleware resolves it to
the real `Session` from the WebSocket connection's `?session=` query
parameter. This is the same pattern that `IStreamServer.PushVideo`
already uses today; the rebind extends it to every quality-control
and read/write method on the new services.

## `control plane`

What remains of the conceptual control plane after Recording and
Playback Quality Control absorb send- and receive-side adaptation is
small: time alignment between media streams, and startup mechanics for
new playback sessions.

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

### Decision Cadence Summary

```text
video buffer       controls playback timing
RpcStream          controls decoder-safe catch-up
recording control  controls outgoing quality (client-local)
playback control   controls incoming quality (client-local)
control plane      controls time alignment + startup
```
