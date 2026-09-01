# Codec performance — measured

Every number here was measured on a real device through WebCodecs, not taken
from a spec sheet or a vendor claim. It is the evidence behind
[`ENCODER_LADDER`](../../src/dotnet/UI.Blazor.App/Services/Video/codec-support.ts)
and the codec policy in [03-codecs-and-layers.md](./03-codecs-and-layers.md).

Measured 2026-08-30/31. Re-measure when a device, browser or codec set changes —
several results here contradict what the same platforms were assumed to do.

## How to read the numbers

**ms/frame is encode time only.** Frames are decoded and held in advance, so
decode never enters the timed window. Each configuration runs a warm-up pass,
then the counters reset and the measured pass is timed from the first
`encode()` to the resolved `flush()`. Three runs, minimum taken.

**The warm-up is not optional.** Hardware encoders spend ~215 ms initialising
on first use, which is most of a short run: without a warm-up every codec and
resolution measures the same and the result is meaningless. Two separate bugs
in this repo came from measuring an encoder mid-startup — the realtime probe
and the sender's throughput deficit — so treat any "they're all identical"
result as a broken harness rather than a finding.

**Budget is 33 ms/frame at 30 fps.** Nothing measured on any device comes
close, even in software. Simulcast does not multiply that by the tier count
either: encode time scales *sub*-linearly with pixels here — on the hardware
paths 480p costs ~two thirds of 720p for 44% of the pixels — so summing all
three measured
resolutions comes to ~2x the 1080p tier alone, and the app's real
W1280/W640/W320 ladder is smaller still. Ample room either way.

**kbps is what the encoder actually produced**, against the target passed in
`bitrate`. It is worth watching: several encoders miss badly in both
directions.

**Content differs between the desktop and phone runs.** Desktop used a real
1080p camera clip (`WIN_20260831…mp4`), lanczos-resized to each resolution.
The phones used generated frames — a moving-bar pattern over a gradient with a
noise band — because ATS blocks plain HTTP from the app WebView and streaming
the clip in as base64 stalled on the usbmux transport. **Phone-to-phone is
directly comparable; phone-to-desktop is not.** Fixing this properly means
shipping the clips in the app's `wwwroot` so they are same-origin.

## Capability matrix

What each engine reports for `VideoEncoder.isConfigSupported`, probing
`prefer-hardware` and `prefer-software` independently.

| | AV1 | HEVC | VP9 | H.264 |
|---|---|---|---|---|
| **Chrome / Windows** (RTX 3090) | hw + sw | hw only | **sw only** | hw + sw |
| **Firefox / Windows** | sw only | **none** | sw only | sw only, *not real-time* |
| **iPhone 13 Pro** (WKWebView) | **none** | hw + sw | hw + sw | hw + sw |
| **Galaxy SM-S948U1** (WebView 151) | sw only | **hw only** | sw only | hw + sw |

No two platforms agree. The only codec every engine can encode is VP9, which
is why it is the negotiation floor.

**The Chromium rows are GPU-dependent, not engine-wide.** "VP9 sw only" and
"HEVC hw only" describe what that machine's GPU exposes through Chromium, and a
different adapter answers differently — which is the whole reason detection
probes each device rather than keying off the browser. The Firefox and WebKit
rows are engine limits and do generalise.

**The `hardwareAcceleration` hint means different things per engine.** On
Chromium the answers are real — an unsupported mode is a genuine gap. On
WebKit they are not: HEVC and VP9 measure identically under both modes (2.98 vs
3.05, 2.27 vs 2.30 at 480p), so WebKit is answering "supported" without
distinguishing. Only H.264 differs there. The echoed `config.hardwareAcceleration`
is worthless everywhere — it mirrors the request — which is why detection asks
each mode separately and believes only the `supported` flag.

## Desktop — Chrome, Windows

Real camera clip, 90 frames, 30-frame warm-up. Targets: 800 kbps at 480p,
1.8 Mbps at 720p, 3.5 Mbps at 1080p.

| codec / profile | 480p hw | 480p sw | 720p hw | 720p sw | 1080p hw | 1080p sw |
|---|---|---|---|---|---|---|
| AV1 Main L4.0 `av01.0.08M.08` | 0.86 | 1.21 | 1.26 | 1.68 | 2.18 | 2.76 |
| AV1 Main L3.0 `av01.0.05M.08` | 0.83 | 1.21 | 1.27 | 1.64 | 1.99 | 2.83 |
| HEVC Main L4.0 `hev1.1.6.L120.B0` | 0.79 | — | 1.19 | — | 2.14 | — |
| HEVC Main L3.1 `hev1.1.6.L93.B0` | 0.82 | — | 1.20 | — | 2.12 | — |
| VP9 P0 L4.1 `vp09.00.41.08` | — | 1.71 | — | 1.97 | — | 3.33 |
| VP9 P0 L3.1 `vp09.00.31.08` | — | 1.64 | — | 2.00 | — | 3.31 |
| H.264 CBP `avc1.42E01F` / `42E028` | 0.87 | 0.77 | 1.32 | 1.01 | 2.15 | 2.06 |

Achieved bitrate, hw / sw: AV1 699/123, HEVC 475/—, VP9 —/550, H.264 511/94 at
480p. The **software encoders undershoot the target badly** — H.264 software
produced 94 kbps against an 800 kbps target — so their apparent speed advantage
at 480p is partly them doing less work at worse quality. Compare within an
acceleration mode, not across. Hardware HEVC undershoots too (475 against 800),
which is the usual quality/rate-control trade and not a fault; hardware AV1 at
699 is the closest to the target.

## iPhone 13 Pro — WKWebView (Voxt Dev build)

Generated frames, 40 frames, 15-frame warm-up.

| codec / profile | 480p hw | 480p sw | 720p hw | 720p sw |
|---|---|---|---|---|
| AV1 (both profiles) | unsupported | unsupported | unsupported | unsupported |
| HEVC Main L4.0 | 2.98 | 3.05 | 4.97 | 5.03 |
| HEVC Main L3.1 | 3.10 | 3.05 | 5.03 | 5.03 |
| VP9 P0 L4.1 | **2.27** | 2.30 | **3.88** | 3.98 |
| VP9 P0 L3.1 | 2.35 | 2.38 | 4.05 | 4.10 |
| H.264 CBP | 1.95 | 2.25 | 4.60 | 7.28 |

Achieved: HEVC 765 / VP9 720 / H.264 827 kbps at 480p (target 800); HEVC 1291 /
VP9 1556 / H.264 1288 at 720p (target 1800).

**VP9 encodes on iOS**, contradicting the common assumption that Apple has no
VP9 encoder — verified by a full encode round-trip (12/12 chunks) in both
acceleration modes, not just by `isConfigSupported`. At 720p it is the fastest
of the three. **AV1 is absent entirely** on the A15.

Decode, verified by round-trip with no `description` supplied:

| | result |
|---|---|
| VP9 | 10/10 frames |
| H.264 CBP | 10/10 frames |
| HEVC | fails — genuinely needs a description |
| AV1 | not advertised |

That HEVC result is the rule `canConfigureWithoutDescription` encodes, confirmed
on a third engine.

## Galaxy SM-S948U1 — Android WebView 151.0.7922.199 (Voxt Dev build)

Same harness and content as the iPhone, so these two tables compare directly.

| codec / profile | 480p hw | 480p sw | 720p hw | 720p sw |
|---|---|---|---|---|
| AV1 Main L4.0 | unsupported | 2.36 | unsupported | 4.44 |
| AV1 Main L3.0 | unsupported | 2.41 | unsupported | 4.65 |
| HEVC Main L4.0 | **1.51** | unsupported | **2.55** | unsupported |
| HEVC Main L3.1 | 1.44 | unsupported | 2.55 | unsupported |
| VP9 P0 L4.1 | unsupported | 2.18 | unsupported | 3.75 |
| VP9 P0 L3.1 | unsupported | 2.89 | unsupported | 3.86 |
| H.264 CBP | **1.17** | 6.69 | **2.43** | 8.59 |

Achieved at 720p against an 1800 kbps target: HEVC 2114, H.264 2509 (hw),
AV1 1724 (sw), VP9 1840 (sw).

Two things worth carrying forward. **The Android hardware encoders overshoot
the bitrate target by 17–39%**, while its software encoders hit it — the ladder
sizes tiers assuming the target is respected. And **software H.264 is the
slowest encoder measured on any device** (6.69 ms at 480p against 1.17 ms for
the same device's hardware path) while compressing worst of the four; that
measurement is why it is no longer a ladder rung.

## Firefox / Windows — latency, not throughput

Firefox was measured for first-output latency rather than the full matrix,
because that is what disqualifies it.

| codec | first output | steady-state depth | verdict |
|---|---|---|---|
| H.264 (every `latencyMode`) | **18 frames** | stays ~18 behind | **not real-time** |
| VP8 | 1 frame | 0 | fine |
| VP9 | 1 frame | 0 | fine |
| AV1 | 1 frame | 0 | fine |

Firefox's H.264 encoder never catches up — it is ~18 frames behind for the
whole stream, roughly 600 ms of added latency, and no configuration changes it.
This is what `probeEncoderLatencyFrames` measures and what removes H.264 from
Firefox's ladder. Firefox also ships **no HEVC encoder at all**.

Decode round-trip with no description: VP9, H.264 and AV1 all 10/10.

For contrast, Chromium's hardware encoders emit their first chunk after ~7
frames / ~215 ms and then track submissions exactly — a startup cost, not a
deficit. Charging that startup to the codec is precisely the bug that
disqualified three working codecs on Chromium.

## What the ladder does with all this

```
hw-AV1 > hw-VP9 > hw-HEVC > sw-VP9 > hw-H.264
```

Software AV1 and software H.264 are deliberately absent. Software H.264 is the
slowest thing measured anywhere and compresses worst of the four. Software AV1
is the narrower call: on the Galaxy — the one device offering both — it is ~18%
slower than software VP9 at 720p (4.44 vs 3.75 ms) while producing *fewer* bits
(1724 vs 1840 kbps), and ~8% slower at 480p. So it buys a CPU loss for no reach
VP9 does not already have, VP9 being the floor every client must decode anyway.
Software HEVC does not exist in Chromium. Firefox drops both MPEG rungs.

Resulting choice per device, verified against each one's real detected
capabilities:

| device | chosen | why |
|---|---|---|
| Chrome / Windows | hw-AV1 | rung 1, available |
| Firefox / Windows | sw-VP9 | no hardware encoder at all; MPEG rungs dropped |
| iPhone 13 Pro | hw-VP9 | no AV1 encoder; VP9 also fastest at 720p |
| Galaxy SM-S948U1 | hw-HEVC | no hardware AV1 or VP9 |

Codec **level** (L4.0 vs L3.0/L3.1) makes no measurable difference on any
device, so the ladder string and the detection probe string differing in level
costs nothing.

## Reproducing this

The harness is a self-contained script that generates or decodes frames, runs
the matrix, and leaves the result in `globalThis.__bench`.

- **Desktop**: serve the clips over a local HTTP server and open the harness
  page in the browser under test.
- **Android**: `adb forward tcp:9334 localabstract:webview_devtools_remote_<pid>`,
  then drive `Runtime.evaluate` over flat CDP. The app's WebView is inspectable
  in the dev build.
- **iOS**: `ios_webkit_debug_proxy -c <udid>:9222`, then wrap every command in
  `Target.sendMessageToTarget` — iOS speaks the target-wrapped WebKit protocol,
  not flat CDP, and `awaitPromise` is not honoured, so the script must park its
  result in a global and be polled. The app must be **foregrounded**, or `/json`
  returns an empty list that looks exactly like Web Inspector being off.
