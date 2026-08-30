---
title: H.264 codec selection fixes
description: Six defects in H.264 profile selection and fallback — we probe one profile and declare another, H.264 can never be excluded, and the receiver gets a single decoder candidate with no software retry.
---

# H.264 codec selection fixes

Status: **decided, not started.** Supersedes the libav.js wasm-fallback plan,
which was dropped — see [Why not libav](#why-not-libav) below.

## Summary

A user report from Firefox on Linux (see [Origin](#origin)) exposed a cluster
of defects in how we pick, declare, and fall back on H.264 profiles. They are
independent of the Firefox bug that surfaced them, and two of them can produce
the same symptom — a frozen video tile — on a browser that behaves correctly.

The through-line: **we advertise a profile we never probed, and then give both
sides only one chance to accept it.**

- Detection probes H.264 **Main 3.1** and reports **High 4.0** as supported.
- `excludeEncoderCodec('h264')` is a hard no-op, so a failed H.264 encoder
  re-picks itself forever.
- The receiver builds **one** decoder candidate for H.264 (HEVC gets eight),
  and only ever probes `prefer-hardware`, never `no-preference`.

Fix order below is by user impact, not by section number.

## Origin

Reported by a user (CachyOS Linux, Firefox 154.0.1) with unusually good
diagnostics. He ruled out the obvious causes himself: FFmpeg 9.0.1, x264 and
OpenH264 2.6.0 all installed, and `ffmpeg -c:v libx264 -profile:v main
-level:v 4.1` encoding 1280×720@30 faster than real time. The OS had a working
H.264 encoder throughout.

What he hit in Voxt:

```text
VideoEncoder.isConfigSupported({codec: "avc1.4D4029", ...}) -> supported: true
[VideoPipeline] encoder error (layer=0, codec=avc1.4D4029, 320x184): Operation is not supported
[VideoPipeline] Recorder pipeline failed: [ENCODER_INIT_FAILED] codec=avc1.4D4029
RecorderWorker: encoder init failure for codec=avc1.4D4029 (category=h264) - excluding and re-picking
repickCodecAndRestart: re-pick returned same codec avc1.4D4029
```

The first half is [Mozilla Bug 1918769](https://bugzilla.mozilla.org/show_bug.cgi?id=1918769)
(Core :: Audio/Video: Web Codecs, UNCONFIRMED, unassigned, P3): Firefox reports
H.264 as supported through `isConfigSupported()` and then fails at
`configure()`. Reported on **Windows and macOS as well as Linux**, so this is
not a Linux-only concern. Related: [Bug 1749047](https://bugzilla.mozilla.org/show_bug.cgi?id=1749047)
— implement `VideoEncoder` on Linux. Note also that Firefox's OpenH264 GMP
plugin is **WebRTC-only** and is not wired to WebCodecs, so no amount of
codec installation fixes this.

The second half — the re-pick loop — is ours, and is fix 1 below.

## Fixes

### 1. `excludeEncoderCodec('h264')` is a hard no-op

`codec-support.ts:468` (and `:490` for `excludeDecoderCodec`):

```ts
export function excludeEncoderCodec(category: string): void {
    if (category === 'h264') return; // never exclude - universal fallback
```

`ENCODER_INIT_FAILED` → `excludeEncoderCodec('h264')` → no-op →
`repickCodecAndRestart` → `getDefaultCodec` returns H.264 again. That is the
`re-pick returned same codec` loop verbatim. The guard encodes an assumption —
that H.264 is a universal floor — which [is false](#wider-codec-strategy).

**Fix:** exclude the specific **codec string**, not the category. `avc1.640028`
failing still leaves `avc1.4D4029` and `avc1.42E01F` to try, so the
"never lose H.264 entirely" intent survives while the loop ends. Pairs
naturally with the probe ladder in fix 2.

Additionally: `repickCodecAndRestart` must refuse to return the codec that
just failed, rather than logging that it did and continuing.

**Alternative:** allow full-category exclusion once VP9 is enabled as a real
floor. Stronger, but only safe after that work lands.

### 2. We probe Main 3.1 and declare High 4.0

`codec-support.ts:150-152`:

```ts
// WebCodecs level ladders are backward-compatible: a working low-level
// profile implies higher ones at the same dims work too.
const chosenCodec = supported ? getCodecForCategory(category, width, height) : codec;
```

The comment is wrong on both axes. `REPRESENTATIVE_CODECS` probes
`avc1.4D401F` (Main 3.1); `getCodecForCategory` returns `avc1.640028`
(High 4.0) on desktop. Main (`0x4D`) → High (`0x64`) is a **profile** change,
not a level step — High is a decode superset but a distinct encoder
capability, and nothing about Main implies it. The level half doesn't hold
either: 3.1 → 4.0 raises max MB rate and DPB size, and `isConfigSupported`
legitimately answers differently.

On any device where Main 3.1 encodes but High 4.0 does not, detection reports
`supported: true, codec: avc1.640028` and `configure()` then fails — the exact
shape of the reported bug, on a browser that never lied.

**Fix (chosen):** probe a short descending ladder per category and report the
first that passes — High 4.0 → Main 4.1 → Main 3.1 → CBP 3.1.
`CODEC_PROFILES.h264` already lists exactly these twelve entries and is
**dead code** (only `CODEC_PROFILES.av1` is read, by `getAV1CodecSupport`), so
the table is free. Costs ~3 extra probes at startup, cached per `WxH`, and
yields a *verified* profile instead of an inferred one.

This dissolves fixes 3 and 5 as side effects.

**Alternatives considered:** probe exactly the string `getCodecForCategory`
returns (minimal, but keeps a guess where a measurement belongs); or drop High
entirely and always use Main (trivially consistent, costs ~5-10% efficiency on
desktop).

### 3. `getDefaultCodec` and `getCodecForCategory` disagree for Firefox

`codec-support.ts:371` short-circuits to `avc1.4D401F` (Main **3.1**), while
`getCodecForCategory` gives Firefox `avc1.4D4029` (Main **4.1**). Two answers
to one question; which one wins depends on whether selection went through
`pickBestCodecByEfficiency` or the `?? getDefaultCodec(...)` fallback.

There is a concrete second-order bug at `video-recorder.ts:1513`:

```ts
const pickedInfo = this.supportedCodecs.find(c => c.codec === pickedCodecString);
this.currentCodecHardwareAccel = pickedInfo?.hardwareAccelerated ?? false;
```

`avc1.4D401F` is never in `supportedCodecs` (detection stores
`getCodecForCategory`'s output), so on Firefox the lookup always misses and
**`hardwareAccelerated` is silently forced to `false`** regardless of the
truth. That feeds bitrate pricing and the QC ladder.

**Fix:** delete the Firefox short-circuit — it is redundant, since
`getCodecForCategory` already branches on `DeviceInfo.isFirefox` and returns
Main. Separately, match by **category** rather than exact codec string at
`:1513` so an unlisted string can never silently mean "software".

The `:1513` change is worth doing regardless of fix 2.

### 4. The receiver has no H.264 candidate ladder and never tries software

`hevc-codec-selection.ts:83` returns a **single** candidate for H.264:

```ts
return [mapCodecToWebCodecs(codec, description)];
```

HEVC gets an eight-deep list (SPS tier, level fallbacks, `hev1`/`hvc1` both
ways). H.264 gets the sender's string verbatim, tried once. Fail
`isConfigSupported` → `selectDecoderCodec` returns `null` →
`initPlayerWorker` bails with `Codec not supported` and the tile never plays —
even though the browser might decode the identical bytes happily under a
different description.

Worse, `selectDecoderCodec` (`:105`, `:114`) only ever probes
`hardwareAcceleration: 'prefer-hardware'`. The **encoder** path
(`isCodecSupported`) tries `prefer-hardware` then `no-preference`, with the
comment *"Firefox often returns false for 'prefer-hardware' but works with
'no-preference'"*. The decoder path is missing exactly that rung, on exactly
the browser it was written for.

**Fix (both, as decided):**

1. Retry `no-preference` when `prefer-hardware` fails. One line; not a new
   feature, a missing rung.
2. Add same-profile-**higher**-level candidates.

Direction matters and is not symmetric. From our own HEVC code
(`hevc-codec-selection.ts:38-42`): Chrome's `isConfigSupported` does *not*
cross-check codec-string level against the description, so `configure()`
succeeds and then `decode()` **silently drops** chunks whose bitstream exceeds
the declared level. So:

- **Over-declaring is safe** — declare High, receive Main, fine.
- **Under-declaring fails silently** — a frozen tile with no error.

Never add a lower-profile candidate without proof the bitstream fits it.

**Alternative (most correct, if needed):** parse the inline SPS from the first
Annex B keyframe for the real `profile_idc` / `level_idc` and build candidates
from ground truth — `hevc-parser.ts` already does this for HEVC, so the
precedent exists (~100 lines). Only worth it if fix 2 doesn't already make the
declared string truthful in the field.

### 5. One declared codec for a three-encoder ladder

`wire-send.ts:266` declares the **top layer's** codec for the whole stream:

```ts
codec: top.metadata.decoderConfig?.codec ?? topCfg.codec,
```

while each simulcast tier has its own `VideoEncoder`, and the receiver
configures one decoder from that string for all layers.

Investigated and **largely a non-issue**, for two reasons:

- `toEncoderConfigs` (`video-recorder.ts:2490`) sets
  `codec: this.currentCodecString` for *every* layer, so all tiers request the
  same profile. Any divergence can only be a browser-side **downgrade** (HW at
  720p, SW OpenH264 at 320×184) — lower, never higher. Max-across-layers
  therefore equals the top tier's today.
- A High-configured decoder decodes everything we can emit. One precision
  worth recording: full **Baseline is not** a subset of Main (it permits ASO,
  FMO and redundant slices, which Main and High do not). **Constrained**
  Baseline is — and our SW string `avc1.42E0xx` is exactly that (`0x42` =
  profile_idc 66, `0xE0` = constraint_set0/1/2 all set), which is also what
  Chrome's SW OpenH264 emits.

**Fix:** log-only. Warn when any layer's `metadata.decoderConfig?.codec`
differs from the top's. Zero behavioural risk; tells us if the assumption ever
breaks.

**Escape hatch if the log fires:** add an optional per-layer `Codec` on the
wire. New field, so old clients are unaffected; absent ⇒ fall back to the
per-stream codec. Do not build this pre-emptively.

### 6. Bare `h264` defaults to High 4.0

`hevc-codec-selection.ts:251-252` and `:267` map bare `'h264'` / `'avc1'` to
`avc1.640028`; `debug-ui.ts:439` hardcodes the same. High is the least
portable of the three profiles in play — a last-resort default should land on
the most portable.

**Fix:** change the default to `avc1.42E01F` (Constrained Baseline 3.1).
Defensive path only; low priority.

## Cleanup found along the way

**In scope, do with the fixes:**

- **Delete `Services/Video/webcodecs-encoder.ts` and `webcodecs-decoder.ts`**
  (~700 lines). Orphaned — they reference only each other; the live path is
  `adapters.ts` + `video-decoder-bridge.ts`. `webcodecs-encoder.ts` is also
  `getCodecForCategory`'s only caller outside `codec-support.ts`, so deleting
  it makes fix 2's rework strictly local.
- **Update `docs/live-video/03-codecs-and-layers.md`**, which documents
  `scalabilityMode?` on `LayerConfig`; the field does not exist and
  `scalabilityMode` appears nowhere in `src/`. Temporal SVC was removed in
  `3ae12d7f8`, not just disabled. Only vestigial `TemporalLayerId` /
  `TemporalLayerCount` remain on the receive-side wire DTO
  (`operators/pull.ts:23`).

**Deferred, worth a look when someone has time:**

- **Firefox MSTP transfer failure** — `startWorker: worker MSTP attempt
  failed ... DOMException: invalid transferable array for structured clone`.
  Falls back to the rVFC pump, so not fatal, but it silently downgrades the
  capture path on every Firefox session. Reproducible locally (Firefox is
  installed on the dev machine), so this is a cheap investigation whenever it
  is picked up — worth pairing with the fix-4 verification pass, which needs
  Firefox anyway.
- **Chromium on Linux takes a consistent 30-40 s** before video stabilises,
  reproduced by the reporter on two machines including a Celeron N3350.
  Separate investigation; no local repro yet.

## Wider codec strategy

Context for why fix 1's guard is wrong, and the open question behind it.

Every Voxt surface is a WebView, so codec support is the engine's:

| Surface | Engine | Media stack |
|---|---|---|
| Web — Chrome/Edge | Chromium | OS HW + bundled SW |
| Web — Firefox desktop | Gecko | system ffmpeg (Linux) / OS |
| Web — Firefox Android | Gecko | **no WebCodecs at all** |
| Web — Safari macOS/iOS | WebKit | VideoToolbox |
| MAUI Android (API 28+) | Chromium (System WebView) | MediaCodec |
| MAUI Windows — WinUI, and the WPF backend | Chromium (WebView2) | Media Foundation |
| MAUI iOS 16.4+ / Mac Catalyst | WebKit (WKWebView) | VideoToolbox |
| MAUI macOS AppKit *(planned)* | WebKit (WKWebView) | VideoToolbox |
| MAUI Linux GTK4 *(planned)* | **WebKitGTK 6.x** | **GStreamer** |

Of the two planned [platform backends](https://learn.microsoft.com/en-us/dotnet/maui/developer-tools/platform-backends/),
macOS AppKit is a codec no-op (its BlazorWebView is WKWebView, same as Mac
Catalyst). **Linux GTK4 is the only new codec environment** — the docs list
`libwebkitgtk-6.0-dev` / `webkitgtk6.0-devel` as a hard prerequisite for
Blazor. WebKitGTK has had WebCodecs since 2.44 (Mar 2024), and 2.48 (Apr 2025)
made it honour `prefer-hardware` as a GStreamer hint.

WebCodecs `VideoEncoder` / `VideoDecoder` support:

| | H.264 | HEVC | VP8 | VP9 | AV1 |
|---|---|---|---|---|---|
| **Chromium** (all platforms) | HW | Win needs HEVC ext | yes | yes | dec yes; HW enc rare |
| **Firefox desktop** | **Bug 1918769 — probe lies** | no | yes | yes | dec yes |
| **Firefox Android** | **no WebCodecs** | no | no | no | no |
| **WebKit** (Safari, WKWebView) | HW | HW | **unverified** | **unverified**, SW only — no Apple VP9 hardware exists | HW-only: M3+, iPhone 15 Pro+ |
| **WebKitGTK** (MAUI Linux) | **distro-dependent** | distro | yes | yes | dec common, enc rare |

WebKitGTK's codecs are whatever GStreamer plugins the distro shipped. VP8/VP9
come from `gst-plugins-good` + libvpx — royalty-free, installed everywhere.
H.264 comes from `gst-plugins-bad`/`ugly`/VA-API — precisely the packages
Fedora, openSUSE and RHEL omit by default, for patent reasons.

So **there is no universal codec and there will not be one.** Apple ships
H.264/HEVC and no VP9 hardware; Linux ships VP8/VP9 and often no H.264. The
`getDefaultCodec` assumption of an H.264 floor is false, which is exactly what
fix 1's guard encodes.

Two open items, neither blocking the fixes above:

1. **Does WebKit do VP8/VP9 in WebCodecs?** Decisive for whether VP9 can be
   the second floor. Sources contradict each other; WebKit has landed VPx
   backends in source, but Apple ships no VP9 hardware so anything present is
   software. Measurable on the Mac mini + tethered iPhone in ~20 minutes:
   `isConfigSupported` **and** an actual `configure()` + encode, since
   Bug 1918769 is precisely a case where the probe lies.
2. **Does the pipeline run on WebKitGTK at all?** Codecs aside, it needs
   module workers, `OffscreenCanvas`, `MediaStreamTrackProcessor` *or*
   `requestVideoFrameCallback`, `VideoFrame`, and either MSTG or a canvas
   backend. Least-tested engine of the set; worth a spike before the Linux app
   is committed to.

### Why not libav

An earlier plan proposed compiling FFmpeg + libopenh264 to wasm (libav.js) as
a software H.264 fallback. Dropped:

- **The premise was wrong.** The reporter's Linux box had a fully working
  H.264 encoder. The failure is a Firefox WebCodecs bug, not a missing codec.
- **Patent exposure.** Voxt currently distributes no H.264 codec — it calls
  WebCodecs, and the browser/OS vendor holds that license. Shipping a wasm
  codec reclassifies Voxt as an AVC codec-unit distributor. Cisco's royalty
  coverage attaches to *Cisco's precompiled binaries* (which is why Firefox
  downloads one), not to openh264 compiled from source; openh264's BSD-2
  licence grants no patent rights at all. The Via LA AVC pool still had ~2,647
  active entries as of 1 May 2026, with the last US pool patent expiring
  Nov 2027.
- **Cost.** ~4.5 MB raw / 1.5 MB gzip, and a call-wide quality tax if the
  profile had to be pinned to Constrained Baseline for everyone whenever one
  libav member joined.
- **Better answers exist.** For MAUI Linux we *package* the app — ship a
  Flatpak/Snap with the GStreamer plugins we want; libvpx is royalty-free, so
  a VP8/VP9 floor costs nothing legally. For web Firefox, the residual case is
  a Mozilla bug plus the fixes above.

If a software fallback is ever needed again, the cheaper routes are
server-side transcode of the one odd leg (which also keeps Voxt from
distributing codec units) or an `RTCPeerConnection` loopback on Firefox, which
runs on the Cisco-licensed GMP plugin.

## Tests

- Unit: detection ladder returns the first *probed-and-passing* profile, and
  never a profile that wasn't probed (fix 2).
- Unit: `excludeEncoderCodec` removes a codec string and `repickCodecAndRestart`
  cannot return it again (fix 1) — regression test for the reported loop.
- Unit: `selectDecoderCodec` falls through to `no-preference`, and candidate
  order never places a lower profile before a higher one (fix 4).
- Manual: Firefox/Linux sender and receiver against Chrome and iOS Safari,
  both directions.

## Docs to update when this lands

- `docs/live-video/03-codecs-and-layers.md` — codec detection, the fallback
  ladder, and the stale `scalabilityMode` reference.
- `docs/live-video/02-sender.md` (encoder rung), `07-receiver.md` (decoder
  candidate selection).
- `docs/api-index-ts.md` — if `webcodecs-encoder.ts` / `webcodecs-decoder.ts`
  are deleted.
