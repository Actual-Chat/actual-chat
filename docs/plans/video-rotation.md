# Video Rotation

End-to-end plan for embedding device-orientation-driven rotation in the live
video pipeline. Two phases: **Phase A** stamps a quantized rotation tag on
every wire frame; **Phase B** uses that tag on the receiver to drive a
cover-vs-inscribe presentation decision plus a blurred backdrop. This doc
covers Phase A in full and sketches Phase B for later.

Status: planning. Phase A approved; Phase B deferred (design only).

## Goal

When the user rotates a phone while shooting live video we want the receiver
to display the picture upright at all times. Today the encoded pixels are
sensor-oriented and the receiver shows them as-is; on iOS the camera always
emits landscape pixels regardless of phone pose, so portrait recording looks
sideways. We fix this by

1. Tracking device orientation while a recorder is running.
2. Quantising it to one of four indices `{0, 1, 2, 3}` = `{0°, 90°, 180°,
   270°}` clockwise, with hysteresis to suppress 90°-boundary flapping.
3. Stamping that index on every encoded frame so receivers can rotate at
   display time.

We do **not** rotate pixels in the sender pipeline; rotation is a tag, not a
transform. The encoder stays at sensor resolution, simulcast layers all
share the same tag, and there is no re-init on flip.

---

## Survey: where rotation lives today

| File | What it does today |
|---|---|
| `Services/Video/services/media-capture.ts:160` | `preferPortraitConstraint()` reads `screen.orientation.type` to decide portrait vs landscape `getUserMedia` constraints. **iOS always gets landscape** (otherwise the encoder flips mid-startup). |
| `Services/Video/operators/rotate.ts` | Sets `frame.rotation` on a Chromium VideoFrame (no-op on Safari). **Not wired into the recorder pipeline.** Will be replaced by `set-rotation.ts` (this plan). |
| `Services/Video/sender/recorder.ts:111-144` | Builds the operator chain: `mstpSource → floodGate → stampCaptureTime → attachSourceDims → downscale → applyKeyframePolicy → encode → wireSend`. No rotation step. |
| `Services/Video/canvas/downscaler.ts:9` | Production downscaler. Comment claims "capture is pre-rotated upstream" — in fact it does not rotate and `drawImage(VideoFrame)` ignores `frame.rotation`. |
| `Services/Video/webgpu/downscaler.ts:381` | WebGPU path that *does* rotate; **not on the production codepath** today. |
| `Services/Video/webcodecs-encoder.ts:190,421` | Logs `frame.rotation` only — diagnostics, no transform. |
| `Api/Video/VideoFrame.cs`, `wire-send.ts` | No rotation field on the wire today. |
| Receiver presenters (`present-canvas.ts`, `present-mstg.ts`) | No rotation awareness. |
| `UI.Blazor/Components/Menu/menu-host.ts:74` | Listens to `screen.orientation` `change` to reposition menus. |
| `UI.Blazor/Components/Bubble/bubble-host.ts:71` | `matchMedia('(orientation: portrait)')`. |
| `UI.Blazor/Services/ScreenSize/screen-size.ts` | Tracks `visualViewport.width/height` and a discrete size enum; **no orientation field**. |

**No central orientation service exists.** Three scattered `screen.orientation`
readers (above) and zero use of `DeviceOrientationEvent` (the accelerometer-
backed device-pose API). Both signals are useful: screen orientation drives
UI reflow (menus, bubbles, getUserMedia constraints); device orientation
drives video rotation **even when the screen is locked** (see [A1](#a1-new-deviceorientation-service)).

## iOS reality check

The platform brief (verbatim under [References](#references)) summarises:

- iOS Safari: `VideoFrame.rotation === null`; pixel buffer is **always
  sensor-landscape**; `displayWidth/Height` may disagree with
  `codedWidth/Height`.
- Android Chrome: `frame.rotation === 0`; buffer already rotated to match
  device pose.
- Desktop: `frame.rotation === 0`; landscape.

For iOS the rotation we want is derived from device pose (see
[A1](#a1-new-deviceorientation-service) for why we don't use
`screen.orientation.angle` directly — OS rotation lock makes the screen
value stale) plus camera facing:

```ts
function iosCameraRotationDeg(screenAngle: number, isFrontCamera: boolean): number {
    return isFrontCamera
        ? (90 + screenAngle) % 360
        : (90 - screenAngle + 360) % 360;
}
```

(Source: WebRTC's `RTCCameraVideoCapturer.m`; the historic
`computeSenderRotation()` in this codebase used only the front-camera branch
and was 180°-wrong on rear-camera landscape.)

Screencast on iOS does **not** follow this formula — screencast buffers
are pre-oriented. We gate the synthesis on `sourceKind === Camera`.

## Priority chain for the per-frame rotation

For each captured frame, in order:

1. **`frame.rotation` if non-null and finite** → trust it (Android Chrome,
   desktop, some Firefox builds).
2. **Else, if `sourceKind === Camera` and `DeviceInfo.isIos`** → synthesize
   via `iosCameraRotationDeg(DeviceOrientation.deviceQuarter * 90, isFrontCamera)`.
3. **Else** → 0.

The result is a degree value; we quantise to `Rotation = 0 | 1 | 2 | 3`
(`Math.round(deg / 90) & 3`) and stamp it.

---

# Phase A — track orientation, stamp per-frame rotation

## A1. New `DeviceOrientation` service

**Location**: `src/nodejs/src/device-orientation.ts` (sibling of
`device-info.ts`, browser-level utility, no UI deps).

**Why "device" and not "screen"**: this module owns *both* the screen-UI
orientation (what `screen.orientation` reports) and the physical device
pose (what `DeviceOrientationEvent` reports). The two diverge when the
user has the OS-level rotation lock on: `screen.orientation.angle` stays
at the locked value while the device is physically rotated. The video
pipeline cares about device pose; UI components care about screen
orientation. One file exposes both.

**Shape** (two classes, one file):

```ts
export type Quarter = 0 | 1 | 2 | 3; // CW quarter-turns from natural portrait

export class ScreenOrientation {
    static get current(): number;      // 0|90|180|270
    static get quarter(): Quarter;
    static get isPortrait(): boolean;
    static change$: Observable<number>;
    static init(): void;
}

export class DeviceOrientation {
    static get current(): Quarter;
    static change$: Observable<Quarter>;
    static init(): void;
}
```

**Behavior**:

- `ScreenOrientation` listens to `screen.orientation` `change` (or
  `window.orientationchange` on older iOS). Synchronous read of
  `screen.orientation.angle`. No DOM measurement.
- `DeviceOrientation` is platform-split:
  - **iOS**: deliberately does NOT subscribe to `deviceorientation` and
    does NOT request motion permission. Falls through to
    `ScreenOrientation.quarter`. **If the user has rotation lock on, that
    locked orientation IS what we keep**: per 2026-05-13 review, locked-
    screen-rotated-phone is not a case we handle in v1.
  - **Non-iOS** (Android/desktop): subscribes to `deviceorientation`
    (no permission prompt needed there). The `beta`/`gamma` axes
    quantize to a quarter-turn (`|beta| > 60°` portrait variants,
    `|gamma| > 60°` landscape variants). Throttled to ~10 Hz before
    quantising; notifies only on quarter-turn crossings. Falls back to
    screen orientation between motion events.

**Why no `ensurePermission`** anywhere: `DeviceOrientationEvent.requestPermission`
is iOS-Safari-only. Skipping it means we never prompt for motion, which
also means we don't need an `NSMotionUsageDescription` in the MAUI iOS
Info.plist. Non-iOS browsers expose `deviceorientation` without any
permission flow.

**Migration of existing screen-orientation subscribers** — all three
move to `DeviceOrientation.screenChange$` (UI-side semantics, not device
pose):

1. `UI.Blazor/Components/Menu/menu-host.ts:74` — already does
   `screen.orientation.addEventListener('change', ...)`. Swap for
   `ScreenOrientation.change$.subscribe(...)`.
2. `UI.Blazor/Components/Bubble/bubble-host.ts:71` — uses
   `matchMedia('(orientation: portrait)')`. Swap to
   `ScreenOrientation.isPortrait` + `change$`.
3. `UI.Blazor.App/Services/Video/services/media-capture.ts:163` — reads
   `screen.orientation.type.startsWith('portrait')`. Swap to
   `ScreenOrientation.isPortrait`.

These three migrations land alongside A1 in the same commit; they're
trivial and leave only the new service touching `screen.orientation`
directly.

**Note**: `ScreenSize` (`UI.Blazor/Services/ScreenSize/screen-size.ts`)
tracks viewport size, not orientation. Different concern, different cadence
(orientation flips are discrete; viewport resizes are continuous). We do
**not** fold orientation into it.

## A2. Rotation quantiser

**Location**: `src/dotnet/UI.Blazor.App/Services/Video/orientation/quantize.ts`
(local — only the video pipeline needs the WebCodecs-flavoured semantics).

Tiny module, no state:

```ts
export type Rotation = 0 | 1 | 2 | 3;   // CW quarter-turns

export function quantize(degrees: number): Rotation {
    const wrapped = ((Math.round(degrees / 90) % 4) + 4) % 4;
    return wrapped as Rotation;
}

// Both branches take an angle (degrees, 0/90/180/270 CW). For iOS we feed
// the **device pose** quarter-turn (DeviceOrientation.deviceQuarter * 90),
// NOT screen.orientation.angle directly — the device-pose value is correct
// even when the OS rotation lock is on. See A1.
export function iosCameraRotationDeg(
    deviceAngle: number,
    isFrontCamera: boolean,
): number {
    return isFrontCamera
        ? (90 + deviceAngle) % 360
        : (90 - deviceAngle + 360) % 360;
}
```

Unit-testable in isolation.

## A3. Rotation debouncer / hysteresis

**Location**: same folder, `rotation-debouncer.ts`.

```ts
export class RotationDebouncer {
    constructor(dwellMs: number);          // default ≈ 200 ms
    feed(target: Rotation, nowMs: number): Rotation;   // returns committed
    readonly committed: Rotation;
    readonly justChanged: boolean;         // true on the frame after a commit
}
```

Behaviour:

- Tracks a committed value and a candidate target.
- A new target must remain stable for `dwellMs` before it's committed.
- During the dwell, every `feed()` returns the still-committed value — wire
  never sees flap.
- `justChanged` is set on the very next `feed()` after a commit, so the
  rotate operator can mark `forceKeyframe = true` and let the receiver
  re-anchor cleanly.

Pure state machine; testable from a sequence of `(t, target)` inputs.

## A4. The `setRotation` operator

**Location**: `Services/Video/operators/set-rotation.ts` (replaces the
unused `operators/rotate.ts`).

**Rename rationale**: the old `rotate.ts` only set `frame.rotation`
(Chromium-only hint). The new operator stamps a `Rotation` tag on the
envelope — works on **all platforms**, including iOS, because the receiver
reads our envelope tag rather than the WebCodecs `frame.rotation` field.
"setRotation" reflects "set the rotation field"; alternatives considered
were `recordRotation` (confusable with the recorder) and `inscribeRotation`
(typesetting connotation).

Stamps every `CapturedFrame` with a `Rotation` tag — does **not** transform
pixels. Sits **right after `mstpSource`**, before `floodGate`. This is the
earliest point where we have a `VideoFrame` and the latest point where the
flood gate can drop it before we spend any work on it.

Inputs (closure):

- `sourceKind: VideoSourceKind` (Camera | ScreenCast).
- `isFrontCamera: boolean` — captured once at recorder start from
  `track.getSettings().facingMode === 'user'` (see
  `video-recorder.ts:600`); the camera-switch path already restarts the
  recorder, so per-frame freshness isn't needed.
- `deviceOrientation`: a reference to the `DeviceOrientation` service.
- `debouncer: RotationDebouncer`.

Per frame:

1. Read raw rotation: `frame.rotation ?? null`.
2. If non-null and finite → use it (Android Chrome, desktop, some Firefox).
3. Else if `sourceKind === Camera` and `DeviceInfo.isIos` →
   `iosCameraRotationDeg(DeviceOrientation.current * 90, isFrontCamera)`.
   On iOS that value tracks `ScreenOrientation` — so it follows the
   user's rotation-lock choice rather than the physical pose.
4. Else → 0.
5. Quantise to `Rotation`.
6. Feed through the debouncer; take the committed value.
7. Stamp `envelope.rotation = committed`.
8. (Optional, best-effort) also write `frame.rotation = committed * 90` so
   any Chromium-only downstream that honours it sees a consistent value.
   Wrap in try/catch — not load-bearing.
9. If `debouncer.justChanged`, set `envelope.forceKeyframe = true` so the
   receiver gets a clean keyframe on the rotated value.

Implementation notes:

- Follow `feedback_video_yield_ownership.md`: `try/finally` with `mustClose`
  on the yield boundary so we don't leak a `VideoFrame` if the consumer
  bails.
- Follow `feedback_nest_single_caller_impl.md`: nest the async generator as
  `impl()` inside the returned `PipeOperator`.
- Reuse `quantize()` and `iosCameraRotationDeg()` from `quantize.ts`.

## A5. Carry the tag through envelopes

Add `rotation: Rotation` to:

- `CapturedFrame`
- `CapturedBundle` (single tag — all layers share one source moment)
- `EncodedFrame`
- `EncodedBundle`

The downscale operator copies the tag from input → each output layer
unchanged. The encoder doesn't touch it. The wire-send operator reads
`bundle.rotation` and writes it on every per-layer DTO.

`canvas/downscaler.ts` and `webgpu/downscaler.ts` need no change — they
already operate per-layer and the tag rides on the envelope, not on the
`VideoFrame` itself.

## A6. Wire DTO

**TS** (`operators/wire-send.ts → VideoStreamFrame`):

```ts
export interface VideoStreamFrame {
    // ... existing fields ...
    rotation?: number;   // 0|1|2|3; omit when 0 to save bytes for legacy senders
}
```

**.NET** (`src/dotnet/Api/Video/VideoFrame.cs`):

```csharp
[DataMember(Order = 18), MemoryPackOrder(18), Key(18)]
public byte Rotation { get; init; }
```

Rationale:
- Per-frame, not per-stream — rotation changes mid-stream.
- `byte` matches `LayerId`/`TemporalLayerId`; values 0–3.
- New `Order/Key` 18; MessagePack/MemoryPack forward-compat skips unknown
  keys, so old receivers ignore the field cleanly.
- `VideoFormat.cs` is **not** touched — format is frozen at stream init;
  rotation isn't.

`CachingVideoFrameFormatter` needs the key added to the read/write order.

`ProcessFrames` in `VideoStreamingBackend.cs` passes the field through
unchanged (no server-side use today).

## A7. Receiver plumbing (no presentation yet)

- `operators/pull.ts` reads `Rotation` from the wire DTO (default 0 for
  missing field).
- Add `rotation: Rotation` to `ArrivedChunk` and `DecodedFrame`.
- The decode operator copies it across.
- Presenters carry it but do **not** apply it yet — that's Phase B.

Add a diagnostic counter (`presentedRotationCounts[4]` or similar) so the
diagnostics modal can show the receiver is seeing non-zero tags from the
sender.

## A8. Test points

- Unit: `quantize()` boundary cases (`-45`, `45`, `89`, `90`, `91`, `135`,
  `225`, `315`, `360`, `-1`).
- Unit: `iosCameraRotationDeg()` for the 8-row table in the iOS brief —
  front × {0,90,180,270} and back × {0,90,180,270}.
- Unit: `RotationDebouncer` with a deterministic clock — flap suppression,
  `justChanged` semantics.
- Manual: phone with Chrome DevTools `Sensors → Orientation` override,
  watch the sender stats for non-zero rotation tag; rotate slowly across
  the 90° boundary and confirm no flap.
- `npm run build:Verify` clean.

## Reuse (per CLAUDE.md type-catalog rule)

**Existing abstractions to reuse**:

- `DeviceInfo` (`src/nodejs/src/device-info.ts`) — `isIos`, `isMobile`.
- `getLogs(...)` for the operator's diagnostics.
- The envelope-yield `try/finally + mustClose` pattern already used in
  `operators/rotate.ts` and others.
- `FrameDropStage` if we ever need to count rotation-related drops.
- `rxjs.Subject` (already used by `ScreenSize`) for `DeviceOrientation.screenChange$` / `deviceChange$`.
- `VideoSourceKind` enum — already on `RecorderConfig`/`recorder-worker-options`.

**Reusability of new components**:

- `DeviceOrientation` → `src/nodejs/src/device-orientation.ts` (shared).
  Migration of the three existing `screen.orientation` callers
  (`menu-host`, `bubble-host`, `media-capture`) is part of A1.
- `quantize.ts` + `rotation-debouncer.ts` → local under
  `Services/Video/orientation/`. No deps on the video envelopes, so they
  *could* live in `actuallab-core`; keeping them local for v1 since the
  semantics (CW WebCodecs/WebRTC convention, dwell defaults tuned for
  capture) are video-specific. Promote later if a second consumer appears.
- `set-rotation.ts` → local under `Services/Video/operators/` (tied to
  `CapturedFrame`).

---

# Phase B — presentation: cover/inscribe + blurred backdrop (deferred)

Documented for context; **do not implement until Phase A is shipping
rotation tags end-to-end**.

## B1. Cover vs inscribe decision

Per frame, with effective post-rotation dims `(fw, fh)` (swap when
`rotation & 1`):

```
loss(cover) = 1 − min(tw·fh, th·fw) / (tw·th)
inscribe iff loss(cover) ≥ 0.20
```

Implementation: switch from CSS `object-fit` rule on `.item-focused` to a
JS toggle written from the presenter (where rotation is known per frame).
Sidebar/PiP tiles stay on cover.

Open: tile-area vs frame-area for the 20% metric. Tile-area planned.

## B2. Blurred backdrop (receiver)

Bring back the worker-painted bg canvas removed in `2bbf4c02e`. The
relevant settings are still in
`Services/Video/services/bg-canvas-settings.ts`:

```
BG_CANVAS_WIDTH = 64
BG_DRAW_INTERVAL_MS = 100
BG_FILTER = 'blur(3px) saturate(1.2)'
BG_BLUR_STRENGTH = 20
```

History to study: `45f32d675`, `f59daac68`, `ffc323dad`, `6cc83df55`.
Sender-side preview (`services/recorder-preview-view.ts`) is the live model
to mirror — paints from the same per-frame stream at 100 ms cadence into a
64×N canvas.

Plug-in points for the rotation tag:
- Canvas backend: `ctx.setTransform(...)` before `drawImage` based on
  `decoded.rotation`.
- MSTG backend (Chromium): re-stamp `frame.rotation = decoded.rotation * 90`
  so `<video>` rotates for us; the worker writes the re-stamped frame to
  the MSTG writable.
- On a rotation change (debouncer `justChanged` mirrored on the wire by
  `forceKeyframe`), the bg painter must invalidate its last bitmap so the
  backdrop doesn't lag the foreground by one orientation.

## B3. Open questions for Phase B

1. **Loss metric**: tile-area (default) or frame-area?
2. **Focused-tile cover allowed?** Today's CSS forces inscribe on
   `.item-focused`; new rule would allow cover when loss < 20%.
3. **Per-tile vs per-stream**: same author in PiP + sidebar should produce
   independent decisions (falls out naturally).
4. **iOS initial offset detection** beyond the `iosCameraRotationDeg`
   formula — needed?

---

## Phase A implementation order

1. `DeviceOrientation` service (`src/nodejs/src/device-orientation.ts`)
   with both screen and device-pose channels + lock inference.
   Migrate `menu-host.ts:74`, `bubble-host.ts:71`,
   `media-capture.ts:163` to the new service.
2. `quantize.ts`, `rotation-debouncer.ts` under
   `Services/Video/orientation/` + unit tests.
3. `operators/set-rotation.ts` (replaces `operators/rotate.ts`); add
   `rotation` to `CapturedFrame`/`CapturedBundle`/`EncodedFrame`/`EncodedBundle`;
   wire it into `sender/recorder.ts` right after `mstpSource`.
5. Wire `isFrontCamera` + `sourceKind` through `RecorderWorkerOptions` so
   the worker can construct the operator.
6. Add `rotation?: number` to `VideoStreamFrame`; pass on every per-layer
   DTO in `wire-send.ts`.
7. Add `byte Rotation` to `Api/Video/VideoFrame.cs` and update the
   `CachingVideoFrameFormatter`.
8. Receiver: read `Rotation` in `operators/pull.ts`, carry through
   `ArrivedChunk` → `DecodedFrame`. Add a small diagnostic counter.
9. `npm run build:Verify`; manual phone test (with rotation lock both on
   and off); ship.

## Open questions for Phase A

1. **Default dwell** for the rotation debouncer. Suggested 200 ms. Confirm
   or override.
2. **Fallback when `screen.orientation` is missing**. Use
   `window.orientation` when present, else assume 0 and log a warning
   once. OK?
3. **Device-motion permission UX on iOS**. `DeviceOrientationEvent.requestPermission()`
   needs a user gesture and shows a permission prompt. Acceptable to
   bundle it silently into the "start camera" tap? Or do we want a
   dedicated explanatory prompt the first time? Recommend silent — the
   capability is auxiliary; denial degrades cleanly to screen-orientation
   only.
4. **Sender-only confirmation**. We're only stamping at the sender — the
   receiver does no orientation work in Phase A (presentation comes in
   Phase B). Confirm that matches intent.

---

## References

- **iOS handoff brief** (companion agent, inlined below) — canonical source
  for the iOS-specific rotation rules. Read this before touching the
  rotate operator.

> ### iOS Camera Rotation — Handoff Brief
>
> **Pipeline context**
>
> - `services/media-capture.ts` — `getUserMedia` constraints + camera
>   selection (`preferPortraitConstraint`, `facingMode`).
> - `services/video-pipeline.ts` (legacy) — owned `senderRotationDeg`,
>   listened to `screen.orientation`, pushed rotation to worker, transposed
>   encoder dims on portrait↔landscape flip.
> - `workers/video-processing.ts` (legacy) — `streamReadLoop` reconciled
>   encoder orientation from incoming frames.
> - `workers/video-encoding-helpers.ts` (legacy) — `resizeFrame` rotate+resize.
> - `webgpu-downscaler.ts` — GPU path; accepted `senderRotationDeg`.
>
> Note: the operator-based pipeline (current code) replaces
> `video-pipeline.ts`/`workers/video-processing.ts`. This plan re-implements
> the rotation logic against the operator pipeline.
>
> **Per-platform `VideoFrame` from MSTP**
>
> | Platform | `frame.rotation` | Pixel buffer | Notes |
> |---|---|---|---|
> | iOS Safari | `null` | Always sensor-landscape | `displayWidth/Height` may differ from `codedWidth/Height`. |
> | Android Chrome | `0` | Already rotated to device pose | Upright. |
> | Desktop | `0` | Landscape | Upright. |
> | Firefox | varies | — | Canvas-fallback restricted to Firefox (commit `600d5ae07`). |
>
> **iOS rules**:
> 1. Sensor is physically landscape (both cameras).
> 2. `VideoFrame.rotation` is null on Safari MSTP — must synthesize.
> 3. Display vs coded dims can disagree; use coded for true layout.
> 4. Never request portrait via `getUserMedia` on iOS (commit `75baae547`).
> 5. Front and back cameras are mounted 180° apart.
>
> **`UIDeviceOrientation` vs `UIInterfaceOrientation`** — browser's
> `screen.orientation.angle` follows `UIInterfaceOrientation` semantics:
>
> | `angle` | Pose | `UIDeviceOrientation` |
> |---|---|---|
> | 0   | portrait | `.portrait` |
> | 90  | landscape, home right | `.landscapeLeft` |
> | 180 | portrait upside-down | `.portraitUpsideDown` |
> | 270 | landscape, home left | `.landscapeRight` |
>
> **Canonical rotation table (CW, WebRTC `RTCCameraVideoCapturer.m`)**:
>
> | `angle` | Front CW | Back CW |
> |---|---|---|
> | 0   | 90  | 90  |
> | 90  | 180 | 0   |
> | 180 | 270 | 270 |
> | 270 | 0   | 180 |
>
> Closed form: see `iosCameraRotationDeg` above.
>
> **Strategy across all platforms**:
> 1. `frame.rotation` if non-null → use it.
> 2. Else for iOS camera → `iosCameraRotationDeg(angle, isFront)`.
> 3. Screencast on iOS: skip the formula — buffers are pre-oriented.
> 4. Re-confirm with dims downstream (post-rotation source dims drive
>    encoder reconfig — commit `6addf8cf7`).
>
> **Checklist**:
> 1. Track `isFrontCamera` from `getSettings().facingMode === 'user'` at
>    start and on camera switch.
> 2. Branch the formula on `isFrontCamera`.
> 3. Skip synthesis when `frame.rotation` is non-null.
> 4. Verify Android Chrome rear-camera path (post-rotation source dims).
> 5. Verify screencast paths in `captureScreencast()`.
> 6. Verify `resizeFrame` rotate90 callers (legacy; n/a for current
>    operator pipeline unless WebGPU downscaler is re-enabled).
> 7. Keep `preferPortraitConstraint` returning `false` for iOS.
>
> **Test matrix**:
>
> | Device | Camera | Pose | Source | Encoder dims |
> |---|---|---|---|---|
> | iPhone Safari | front | portrait | `senderRotationDeg=90` (synth) | portrait |
> | iPhone Safari | front | landscape (home left) | `=0` | landscape |
> | iPhone Safari | rear  | portrait | `=90` | portrait |
> | iPhone Safari | rear  | landscape (home right) | `=0` | landscape |
> | Android Chrome | front | portrait | `frame.rotation=0` + portrait buffer | portrait |
> | Android Chrome | rear  | landscape | `frame.rotation=0` + landscape buffer | landscape |
> | Desktop | webcam | landscape | `frame.rotation=0` | landscape |
>
> **Key commits**: `628d2af7d`, `936ed3b67`, `07bd78d08`, `6bc45ebc4`,
> `6addf8cf7`, `9c2ffe65c`, `75baae547`, `5652de5f8`, `b63e58d70`.
