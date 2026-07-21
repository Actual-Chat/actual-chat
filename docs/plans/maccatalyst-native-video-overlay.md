# Mac Catalyst: native video rendering via AVSampleBufferDisplayLayer

> **How to read this.** Each section opens with a **bold one-liner**; read just
> those for the 2-minute version. The TL;DR + phase table below are the
> 30-second version. Everything after a one-liner is depth for whoever
> implements that part.

## Implementation status (2026-07-16)

**Phases 1–2 built and compiling; off by default; needs on-device bring-up.**
The remote-tile path is implemented end-to-end and gated behind
`MauiSettings.UseNativeVideoOverlay` (default `false`). Flip it to `true` and
rebuild for on-device testing — nothing changes while it's off (the JPEG path
stays the default). New/changed pieces:

- `H264SampleBufferBuilder` — Annex-B → AVCC + format latch extracted from
  `AppleVideoDecoder` (which now reuses it); builds the `CMSampleBuffer`s both
  the decode path and the layer path consume.
- `SampleBufferDisplayView` — `UIView` backed by `AVSampleBufferDisplayLayer`;
  enqueue / flush / decoder-failure recovery.
- `NativeVideoOverlayHost` (+ `INativeVideoOverlayHost`) — pins layer views over
  the WKWebView by id; `UpdateRect` from JS. **Placement defaults to
  AboveWebView (overlay-on-top)** — chosen for bring-up because it shows pixels
  regardless of DOM/webview transparency; the participant label is covered until
  we switch to underlay. (`WKWebView.Opaque` is already `false`, so underlay is
  viable — flip `NativeVideoOverlayHost.OverlayPlacement`.)
- `MauiVideoLayerPlayer` — `INativeOverlayVideoPlayer`; same pull loop as
  `MauiVideoPlayer`, enqueues to its display view instead of emitting JPEG.
  `MauiVideoPlayerFactory` picks it when the host is registered.
- `native-video-overlay.ts` — rAF rect tracker (getBoundingClientRect →
  `OnOverlayRect` on the component → host), IntersectionObserver for visibility.
- `VideoTrackPlayer.razor` + `video-panel.css` — overlay branch, `OnOverlayRect`
  JSInvokable, `.native-video-overlay` tile class (canvas kept in layout as the
  rect anchor, backgrounds made transparent).

**Still to do on-device (Phase 0 decision + Phases 3–4):**
1. Turn the flag on, confirm a remote tile renders, and **calibrate placement**:
   verify the CSS-px rect maps 1:1 to the container view frame (webview pinned at
   offset 0 → CSS px == points; confirm no inset/scale surprise).
2. Decide **AboveWebView vs BelowWebView** (underlay) per the trade-offs below;
   if underlay, audit `.video-track-player` ancestor backgrounds for opacity.
3. Phase 3 (self-preview on capture-fed layers) and Phase 4 (polish + deleting
   the JPEG path) are not started.

## TL;DR

Today every displayed video frame on Mac Catalyst crosses from native code
into the WKWebView as a base64 JPEG over Blazor JS interop (~MJPEG-over-interop,
640px max, ~2× JPEG transcode per frame per tile). This plan replaces that
display transport with `AVSampleBufferDisplayLayer` — a CALayer that accepts
the compressed H.264 `CMSampleBuffer`s we already build and does hardware
decode + GPU display itself. The wire pipeline (capture → VideoToolbox encode →
`PushStream`, `GetStream` → frames) does not change; only the last hop
(pixels → screen) does. The hard part is not video — it is **placement**:
keeping native layers aligned with DOM tiles through scroll, resize, z-order,
and Blazor re-renders. The plan spends Phase 0–1 de-risking exactly that.

| Phase | Outcome | Size |
|---|---|---|
| 0 | Spike: one hardcoded overlay plays a live stream; placement strategy picked | S |
| 1 | Overlay infrastructure: JS rect tracker + native overlay host, no product change | M |
| 2 | Remote tiles render via ASBDL; JPEG path deleted for remote playback | M |
| 3 | Self-preview tiles (in-call + join modal) on capture-fed layers | S–M |
| 4 | Polish: occlusion, corners, mirroring; delete the remaining JPEG machinery | S |

Fallback at every phase: the current JPEG path stays intact until Phase 2/4
delete it, so the feature can ship (or stall) per-phase without regressions.

## Goal

**Replace the JPEG-over-interop display transport with native layers, keeping
the rest of the pipeline untouched.**

- Remote tiles: full-resolution, hardware-decoded, GPU-composited video
  instead of ≤640px JPEG at quality 0.7.
- Self-preview tiles: zero-copy preview straight from the capture session.
- Delete per-frame interop: JPEG encode → base64 → dispatcher hop → `atob` →
  JPEG decode → canvas draw, per frame, per tile.

## Non-goals

**This is a Mac Catalyst display change only.**

- No changes to the wire format, server fan-out, simulcast, or quality control.
- No iOS enablement (the code compiles for both — see Reuse — but iOS keeps
  the JS/WebCodecs pipeline until separately validated).
- No native ScreenCast rendering changes beyond what remote tiles get for free.
- Not addressing the publisher-side gaps (`SetDemandedLayers` etc. no-ops) —
  separate work.

## Current state

**Three surfaces push base64 JPEG frames into canvases; all three exist only
because the WKWebView can neither decode our streams nor see the camera.**

| Surface | Native side | Web side |
|---|---|---|
| Remote tile | `MauiVideoPlayer` (`GetStream` → `AppleVideoDecoder` → `JpegFrameEmitter`) | `video-track-player-native.ts` → `canvas.remote-video` |
| In-call self-tile | `AppleCameraPreview` tapping `AppleCameraFrameTap` | `video-streaming-preview.ts` |
| Join-modal preview | `AppleCameraPreview` (own `AVCaptureSession` pre-publish) | `join-video-call-modal.ts` |

Cost per frame per tile: CoreImage downscale + JPEG encode (native), base64
(~+33%), a Blazor-dispatcher `InvokeAsync` hop, `atob` + `createImageBitmap`
(a JPEG decode) in JS, canvas draw. The `JpegFrameEmitter` in-flight gate
caps it by dropping frames when interop is slow, which also caps quality.

Key seams already in place (see `docs/live-video/07-receiver.md`):

- `INativeVideoPlayer` / `INativeVideoPlayerFactory`
  (`src/dotnet/UI.Blazor.App/Services/Video/INativeVideoPublisher.cs`) —
  `VideoTrackPlayer.razor` delegates to it on Catalyst (`StartNativePlayer`).
- `AppleVideoDecoder` (`src/dotnet/App.Maui/MaciOS/Video/AppleVideoDecoder.cs`)
  already converts wire Annex-B → AVCC and builds `CMVideoFormatDescription` +
  `CMSampleBuffer` (`DecodeAvcc`) — exactly what ASBDL consumes.
- `AppleCameraFrameTap` fans out the publisher's captured `CMSampleBuffer`s.
- `MauiWebView.MaciOS.cs` owns the platform `WKWebView`
  (`SetPlatformWebView`, `OnInitialized`) — the place to attach native layers.

## Options considered

**AVSampleBufferDisplayLayer overlays are the target; MJPEG-over-URL-scheme is
the documented fallback if placement proves intractable.**

1. **Keep JPEG-over-interop** (status quo). Works, capped at ~640px/15–30fps,
   CPU cost scales linearly with tiles. No further action. Baseline.
2. **MJPEG via `WKURLSchemeHandler`** — serve `multipart/x-mixed-replace`
   JPEG streams on a custom scheme (a handler pattern already exists:
   `ContentSchemeHandler`, `MauiWebView.MaciOS.cs:68,184`); tiles become
   `<img src="voxt-video://stream/...">`. Removes base64 + per-frame interop +
   dispatcher hops; keeps JPEG transcode and the resolution cap. **No placement
   problem at all.** Choose this if Phase 0 kills the overlay approach.
3. **`AVSampleBufferDisplayLayer` overlays** (this plan). Feed compressed
   samples; the layer hardware-decodes and displays. Deletes the decoder
   session, the JPEG transcode, and all per-frame interop. Costs placement
   infrastructure (Phase 1).

## Reuse

### Existing abstractions to reuse

- `INativeVideoPlayer` / `INativeVideoPlayerFactory` — the new layer-backed
  player implements the same interface; `VideoTrackPlayer.razor` keeps its
  existing gate. The `Func<byte[], ValueTask> onFrame` callback becomes unused
  in the layer path (see Open questions #3).
- `AppleVideoDecoder.TryConfigure` / `ToAvcc` / `EnumerateNals` — the
  Annex-B → AVCC + parameter-set latch is reused verbatim; only
  `VTDecompressionSession.DecodeFrame` is replaced by `layer.Enqueue`.
- `AppleCameraFrameTap` — self-tile layer feeds from the same tap; the
  Phase-3 preview layer is one more subscriber, no capture changes.
- `MauiVideoPlayer.PullLoop` — `GetStream` + reconnect + `RequestKeyFrame`
  logic carries over unchanged.
- `MauiWebView` (`App.Maui/WebView/MauiWebView.MaciOS.cs`) — host for the
  overlay container view; already exposes the platform `WKWebView`.
- JS: `ResizeObserver`/`IntersectionObserver` usage patterns exist in
  `src/dotnet/UI.Blazor.App/Services/Video/services/tile-fit.ts` and
  `canvas-target.ts`; the rect tracker follows the same conventions.
- No fitting existing abstraction exists for "track a DOM rect and mirror it
  to native" — searched `docs/api-index-ts.md` (`observer`, `rect`, `overlay`)
  and `docs/api-index.md`; this is genuinely new (see below).

### Reusability of new components

- **`NativeViewRectTracker` (TS)** — watches an element's viewport rect +
  visibility and reports changes over interop. Useful beyond video (any
  future native overlay: maps, PiP). → Place in
  `src/dotnet/UI.Blazor.App/Services/` (shared services folder), not under
  `Components/VideoPanel/`. Not `src/nodejs/` — it depends on Blazor interop
  conventions specific to `UI.Blazor.App`.
- **`NativeOverlayHost` (C#)** — maps logical overlay ids → UIViews pinned to
  reported rects. Apple-specific by nature. → `App.Maui/MaciOS/` top level
  (not `Video/`), since it is not video-specific. Cross-platform abstraction
  (`ActualChat.Core`) is *not* proposed: Windows/Android have no analogous
  need today; promoting later is cheap because the JS half is already shared.
- **`SampleBufferDisplayView` (C#)** — UIView wrapping ASBDL + flush/error
  handling. Video-specific → `App.Maui/MaciOS/Video/`.

## Target architecture

**One native overlay container sits above (or below — Phase 0 decides) the
WKWebView; JS reports each tile's rect; native pins a display layer to it;
frames never cross the interop boundary.**

```
GetStream ──► MauiVideoPlayer.PullLoop ──► AnnexB→AVCC + format latch
                                             (AppleVideoDecoder, reused)
                                                  │ CMSampleBuffer (compressed)
                                                  ▼
                     NativeOverlayHost ◄── SampleBufferDisplayView(ASBDL)
                          ▲ rect updates
                          │ (interop, ~event-rate not frame-rate)
   VideoTrackPlayer.razor ─► NativeViewRectTracker (ResizeObserver/scroll/IO)
```

Placement strategy — decided in Phase 0, two candidates:

- **A. Overlay-on-top**: container UIView added as a sibling *above* the
  WKWebView. Web content can never draw over a tile → tile chrome (name
  label, mute icons — currently web-rendered on top of the canvas) must be
  hidden while the layer is live, or re-rendered natively, or the layer must
  be hidden whenever a modal/menu opens (tracked via existing UI state:
  `ModalUI`/history). Simple to build, ugly edge cases.
- **B. Underlay (transparent hole)**: layers *behind* a transparent-background
  WKWebView; the tile element (and every ancestor covering its rect) gets a
  transparent background on Catalyst, so the native video shows through while
  labels/menus/modals naturally composite on top. Elegant z-order, but
  requires `webView.Opaque = false` + auditing ancestor backgrounds under
  `.video-track-player` (theme CSS) — risk of visual artifacts elsewhere.

Phase 0 must produce a working sample of the preferred candidate; B is
preferred on merit if the transparency audit is tractable.

## Phases

### Phase 0 — Spike (de-risk placement + enqueue path)

**Prove, with throwaway code, that (a) a compressed-fed ASBDL renders our real
stream, and (b) one placement strategy survives scroll/resize/modal.**

- Hardcode: one `AVSampleBufferDisplayLayer` in a UIView pinned over the first
  remote tile, fed by a copy of `MauiVideoPlayer` that enqueues instead of
  decoding (`kCMSampleAttachmentKey_DisplayImmediately = true` on every
  sample; `Flush()` on keyframe-triggered format change).
- Try underlay (B): flip `Opaque=false`, transparent body + tile chain in a
  Catalyst-only stylesheet, eyeball artifacts across light/dark themes.
- Exit criteria: live video visible in-place; verdict A vs B vs "abort to
  MJPEG option"; measured CPU vs JPEG path (expect large drop); notes on
  `requiresFlushToResumeDecoding` behavior on layer switches.
- Nothing merges; findings land as an addendum to this doc.

### Phase 1 — Overlay infrastructure

**Build the reusable rect-sync plumbing with a debug-colored placeholder view;
no user-visible change.**

- TS `NativeViewRectTracker` (`UI.Blazor.App/Services/`): per element —
  `ResizeObserver` + scroll listeners (capture phase, rAF-throttled) +
  `IntersectionObserver` for visibility; reports
  `{id, x, y, w, h, visible, radius}` in device pixels only on change.
- C# `NativeOverlayHost` (`App.Maui/MaciOS/`): container view attached in
  `MauiWebView.OnInitialized`; API `Attach(id, UIView)`, `Update(id, rect)`,
  `Detach(id)`; all UI-thread marshalled.
- Interop route: reuse the existing JS→.NET path (`DotNetObjectReference`
  like `VideoTrackPlayer`'s callbacks) — rect updates are event-rate, not
  frame-rate, so ordinary interop is fine.
- Exit criteria: a debug rectangle tracks a tile within one frame during
  scroll/resize/panel toggle; occlusion rule from Phase 0 implemented
  (hide-on-modal for A, nothing needed for B).

### Phase 2 — Remote tiles on ASBDL

**Swap the remote-tile pixel path; keep everything else identical.**

- `SampleBufferDisplayView` (`App.Maui/MaciOS/Video/`): wraps ASBDL;
  `Enqueue(CMSampleBuffer)`, flush-on-discontinuity, `.Failed` status →
  re-request keyframe (existing `RequestKeyFrame` call in `PullLoop`).
- `MauiVideoPlayer`: replace `AppleVideoDecoder` decode-to-pixels +
  `JpegFrameEmitter` with sample-buffer construction (reuse
  `TryConfigure`/`ToAvcc`) + `Enqueue`. Layer switches: on new SPS/PPS,
  `Flush()` then enqueue from the keyframe (mirrors today's decoder
  reconfigure).
- `VideoTrackPlayer.razor`: `StartNativePlayer` additionally registers
  `CanvasRef`'s container with `NativeViewRectTracker`; the canvas becomes the
  placement anchor (and stays as poster/fallback surface).
- Delete: `video-track-player-native.ts` render path (keep the class as a
  thin shim only if the fallback below stays), `JpegFrameEmitter` usage in
  `MauiVideoPlayer`.
- Feature flag: keep the JPEG path selectable via a debug setting for one
  release (same pattern as `?bgBlur=off` kill switches,
  `bg-blur-override.ts`) so field regressions can be bisected.
- Exit criteria: E2E rig (app on Catalyst + Chrome publisher) shows remote
  tile at source resolution; tile survives scroll, focus-mode toggle, panel
  resize, modal open; sustained 10+ min; CPU measurably below baseline.

### Phase 3 — Self-preview on capture-fed layers

**Feed preview layers straight from the capture side; no encoded data, no
interop.**

- In-call self-tile: a `SampleBufferDisplayView` subscribing to
  `AppleCameraFrameTap` (pixel-buffer samples enqueue fine), replacing
  `AppleCameraPreview`'s JPEG path in `VideoStreamingPreview.razor`.
- Join modal: `AVCaptureVideoPreviewLayer` attached to the preview's own
  `AVCaptureSession` (`AppleCameraPreview._ownCapture`) — zero-copy, and the
  session handover on `SourceChanged` keeps working (the layer just goes
  stale-frame during handover, same as today).
- Mirroring: `transform = CATransform3DMakeScale(-1,1,1)` driven by the same
  `CameraUI.GetIsMirrored` state the CSS uses today.
- Exit criteria: modal preview + self-tile render with no `[JPEG]` frames in
  logs; camera switch and modal→publisher handover verified on device.

### Phase 4 — Polish & deletion

**Make it invisible, then delete the scaffolding.**

- Rounded corners (`cornerRadius` from the reported `radius`), theme-checked
  in light/dark, focused/unfocused tiles.
- Remove the feature flag; delete `JpegFrameEmitter`, `AppleCameraPreview`'s
  encode path, `renderJpegFrame` + `jpeg-frame-renderer.ts`,
  `video-track-player-native.ts`, and the `renderPreviewFrame` JS methods —
  unless the MJPEG fallback was shipped anywhere (then they stay behind it).
- Update `docs/live-video/07-receiver.md` (receiver section) and the
  Mac-Catalyst notes to describe the layer path.

## Risks & mitigations

**Placement is the risk; everything video-side is well-trodden.**

| Risk | Impact | Mitigation |
|---|---|---|
| Underlay transparency breaks theme visuals | Phase 0 rework | Strict Catalyst-only CSS scope; fall back to overlay-on-top (A) |
| Overlay-on-top hides web chrome over tiles | UX regression | Hide-layer-on-modal via `ModalUI`; move tile label natively only if unavoidable |
| Rect lag during fast scroll | Cosmetic jitter | rAF-throttled updates; acceptable per Phase 1 exit criteria; worst case: fade layer during scroll |
| ASBDL rejects our SPS (low-latency bitstream) | Blocks Phase 2 | Phase 0 tests with real streams; decoder path already validated the bitstream with VTDecompressionSession — same decoder underneath |
| Layer switch (simulcast) glitches | Visible flicker | `Flush()` + enqueue-from-keyframe; server already sends keyframe on switch (docs/live-video/06) |
| Blazor re-render churn leaks layers | Memory/decoder leak | `Detach` in `DisposeAsync` mirrors existing `_nativePlayer` disposal; assert host map empty in debug builds |
| Many tiles × decoders exhaust HW decode | Degraded calls | Same budget as today's VTDecompressionSessions — no worse; monitor via existing diagnostics panel |

## Open questions

1. **A or B placement** — owned by Phase 0. Everything downstream is agnostic.
2. **Poster/last-frame behavior** — when a stream pauses, today the canvas
   keeps the last JPEG. ASBDL keeps its last frame too, but on `Flush` it
   clears; decide whether to snapshot into the canvas before flushing.
3. **`INativeVideoPlayer` shape** — `Start(Func<byte[], ValueTask>)` is
   JPEG-specific. Phase 2 should change it to `Start()` +
   overlay-id parameter (breaking only for the two implementations we own).
4. **Screen-recording/screenshot capture** of native layers inside the app
   window (used by the E2E screen-diff checks) — verify `screencapture` picks
   up AVSampleBufferDisplayLayer content; if not, adjust the rig to assert on
   logs + layer status instead.

## Verification

**Each phase has device-level exit criteria; the standing E2E rig covers the
full loop.**

- Build: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-maccatalyst`
  (+ `npm run build:Verify` for TS); run via `/macos-run`.
- E2E: Catalyst app + a Chrome viewer/publisher on a shared chat (the rig used
  for #3962 bring-up): outbound + inbound simultaneously, 10+ min soak,
  layer-switch exercised by resizing the viewer tile (drives server layer
  selection), reconnect exercised by toggling network.
- Perf: compare `top`/Instruments CPU of the app process at 1 and 4 remote
  tiles vs the JPEG baseline measured in Phase 0.
- Regression: iOS build still compiles (code is in `MaciOS/**`) and iOS
  behavior is unchanged (factories remain Catalyst-gated in
  `MauiAppModule`).
