# WebRTC sender backend — power & CPU profiling vs WebCodecs

Comparison of the experimental **WebRTC encoder-tap** sender backend against
the production **WebCodecs + metadata downscaler** path, from two Perfetto
system traces captured on the same Android device.

**Date:** 2026-06-17
**Verdict (this device, these sessions):** the WebRTC backend used **more**
power, not less — ~+7% CPU busy-time at **+40% sustained core clock**. The
backend removed the JS-downscaler / GPU-readback cost it was designed to kill,
but the loopback + simulcast camera-feed plumbing more than ate the savings.

> Directional, not a verdict. See [Caveats](#caveats).

## Traces

| label    | file                                       | backend                          | window |
|----------|--------------------------------------------|----------------------------------|--------|
| metadata | `mobile-metadata.trace` (517 MB, 06-10)    | WebCodecs + metadata downscaler  | 64.0 s |
| webrtc   | `mobile-webrtc.trace`   (669 MB, 06-17)    | WebRTC single-PC native simulcast | 60.1 s |

Perfetto protobuf system traces (ftrace `sched` + `cpufreq`, Chrome/WebView
slices). **No hardware power rails** — power is proxied from CPU on-time and
frequency residency. App process `chat.actual.dev.app` (MAUI WebView host) +
renderer `com.google.android.webview:sandboxed_process0`.

Analysis tooling: Perfetto `trace_processor` (Python). Scripts kept under
`tmp/profiles/` (`analyze.py`, `freq.py`). The `trace_bounds` duration is bogus
(clock-domain mixing → ~34 h); the real window is the `sched_slice` span.

## Overall (sched + cpufreq)

| metric                          | metadata (WC) | webrtc | Δ         |
|---------------------------------|---------------|--------|-----------|
| busy core-seconds               |       263     |   280  | **+6.6%** |
| avg busy cores                  |      4.11     |  4.67  | +14%      |
| **cpu0–5 avg freq (MHz)**       |    **1396**   | **1955** | **+40%** |
| cpu6–7 avg freq (MHz, ~idle)    |       891     |   912  | flat      |
| cpu0–5 utilization              |    65–75%     | 75–77% | higher    |
| freq-weighted dyn-power proxy   |     10120     | 13531  | **+34%**  |
| GPU completion / s              |      65.6     |  59.6  | −9%       |

The per-CPU frequency is the decisive signal (verified per-CPU, not per-track —
cpufreq track IDs are not comparable across traces). WebRTC held the working
cluster cpu0–5 at **1955 vs 1396 MHz**. Dynamic power ∝ C·V²·f and V rises with
f on DVFS, so a 40% freq bump costs **well more than 40% energy** on those
cores — on top of +7% busy-time. The +34% freq-weighted proxy is the
conservative floor; true energy delta is larger.

## Where the work moved (per-thread on-CPU, core-seconds)

**WebRTC ADDED:**

| thread                                 | meta | webrtc | note          |
|----------------------------------------|------|--------|---------------|
| `app / VideoCaptureCam`                |  3.0 |  9.3   | **3×**        |
| camera HAL (`qti.camera.provider`)     | 61   | 76     | +24%          |
| `media.hwcodec` (native encoder svc)   |  0   |  3.3   | **new**       |
| `webview / WebRTC_W_and_N`             |  0   |  2.8   | new           |
| `app / NetworkService` (loopback RTP)  |  1.1 |  2.5   | +127%         |
| `webview / ThreadPoolForegroundWorker` |  1.2 |  4.8   | +300%         |
| `webview / VideoFrameCompositor`       |  2.5 |  4.4   | +76%          |

**WebRTC REMOVED** (the design goal — kill the JS downscaler + GPU readback):

| thread                                      | meta | webrtc | note   |
|---------------------------------------------|------|--------|--------|
| `webview / DedicatedWorker` (JS downscaler) |  2.9 |  1.2   | −59%   |
| `app / main` (`.actual.dev.app`)            |  8.5 |  5.4   | −36%   |
| `app / RenderThread`                        | 18.8 | 16.2   | −14%   |
| `app / VizWebView` (compositing)            | 11.6 |  8.9   | −23%   |
| `webview / Media`                           |  5.3 |  3.9   | −26%   |
| GPU completion rate (/s)                    | 65.6 | 59.6   | −9%    |

## Mechanism

On the **encode side** the backend behaved as intended: JS DedicatedWorker (the
metadata downscaler), main thread, Viz compositing and GPU completion all
dropped. Those savings were **outweighed** by the loopback plumbing — single-PC
native simulcast feeds the camera frame into the HW encoder + RTP, and the
**camera-capture path blew up 3×** (`VideoCaptureCam` + camera HAL).

**Prime suspect: `VideoCaptureCam` 3×.** Likely the loopback PC pulls the camera
at steady full framerate and fans one frame out to 3 simulcast encoders, while
the WebCodecs path applies adaptive-fps / temporal pacing (`temporalPace` in
`video-recorder.ts` — see [02 — Sender](02-sender.md)). If the WebRTC sender
does **not** honor the same fps pacing, capturing/feeding full-rate where
WebCodecs paced down would alone explain the cluster boost. **Next step:** pull
the `VideoCaptureCam` slice breakdown + capture fps / per-frame cost in each
trace to confirm.

## Caveats

- One session each; whole-system traces (other apps' noise included, though
  the per-thread numbers above are filtered to app + webview).
- **No hardware power rails** — proxied via cpufreq + sched. Treat the absolute
  proxy values as relative-only.
- Background apps differ between runs; camera HAL cost swings with lighting / AE.
- The direction (WebRTC heavier here) is solid because the frequency and
  utilization signals are large and mutually consistent; the exact multiplier
  is not.

## Takeaway

The WebRTC encoder-tap backend is **not** a power win on this device as-is. The
encode-side savings are real but the camera-feed + loopback + RTP overhead
dominates. Before pursuing it as a production path, confirm and fix the
`VideoCaptureCam` 3× — most plausibly by making the WebRTC sender honor the same
adaptive-fps pacing the WebCodecs backend uses.
