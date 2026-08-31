---
title: Codec negotiation — per-client encode assignment
description: VP9 turns out to be real-time on every engine we ship to, so it becomes the default; clients report decode and encode sets separately and the server assigns each an encode codec by coverage for the mixed cases that remain.
---

# Codec negotiation — per-client encode assignment

Status: **designed, approved, not started.** Successor to
[H.264 codec selection fixes](./h264-codec-selection-fixes.md), which is
implemented on `fix/h264-codec-selection` (PR #4303).

This document is a handoff: it carries every measurement the design rests on,
so none of it has to be re-derived.

## Why

Codec capability is **asymmetric per device** — what a browser can decode and
what it can encode are different sets — and the current API reports only one of
them. `RegisterMember(session, chatId, supportedDecoderCodecs)` sends decode
support; the server intersects those and hands back one list; each sender then
filters it by its own encoder support locally.

That breaks in both directions:

- A device that can *decode* a codec but not *encode* it (every Apple device
  with AV1; Firefox with H.264 in practice) is treated as if it could send it.
- A device that can encode something nobody else decodes has no way to find
  that out except by failing.

The measurements below make it concrete.

## Measured facts

All measured 2026-08-30 against the running dev server. Firefox numbers are
Firefox 154 on Windows 11, driven over WebDriver BiDi (`tmp/ff-bidi.mjs`).

### Firefox H.264 encode is unusable for real-time

First `EncodedVideoChunk` arrives only after **18 submitted frames** — roughly
570 ms at 30 fps. No configuration changes it:

| config | frames before first output |
|---|---|
| `latencyMode: 'realtime'` | 18 |
| `latencyMode: 'quality'` | 18 |
| `realtime` + `bitrateMode: 'constant'` | 18 |
| no `latencyMode` | 18 |
| `scalabilityMode: 'L1T1'` | **not supported** |
| **VP8** `realtime` | **1** |
| **VP9** `realtime` | **1** |
| **AV1** `realtime` | **1** |

So Firefox *has* H.264 encoding; it is simply half a second behind. VP8, VP9
and AV1 on the same browser emit on the first frame.

### Firefox H.264 encode also rejects prefer-hardware

For `avc1.640028`, `avc1.4D4029`, `avc1.4D401F`, `avc1.42E01F` at 1280×720:

| `hardwareAcceleration` | `isConfigSupported` | real `configure()` + `encode()` |
|---|---|---|
| `prefer-hardware` | false | `NotSupportedError` |
| `no-preference` | true | works |

**Decoding is unaffected** — H.264 decode probes true under both. Probe and
reality agree, so [bug 1918769](https://bugzilla.mozilla.org/show_bug.cgi?id=1918769)
does not reproduce on Windows (the original report was Linux, where it does).

### Desktop Safari does VP8 and VP9 — in both directions

Safari 26.6 on the Mac mini, `isConfigSupported` for 640×360:

| codec | decode | encode |
|---|---|---|
| H.264 | yes | yes |
| HEVC | yes | yes |
| **VP8** | **yes** | **yes** |
| **VP9** | **yes** | **yes** |
| AV1 | no | no |

Confirmed by real `configure()` + `encode()`, measuring frames to first output:

| codec | first output after | first chunk |
|---|---|---|
| H.264 | 1 frame | 124 B |
| HEVC | 2 frames | 488 B |
| **VP8** | **1 frame** | 450 B |
| **VP9** | **1 frame** | 212 B |

**This overturns the "there is no universal codec" premise.** VP9 encodes and
decodes at real-time latency on Chromium, Firefox *and* Safari. It is a
genuine candidate for the call-wide codec rather than a Firefox-only escape
hatch — which makes step 3 (enable VP9) the highest-value change in this plan,
not a supporting one.

Two caveats before relying on it:

- Apple has no VP9 hardware, so this is a software encoder. Latency is fine;
  **sustained CPU at 720p30 is unmeasured** and is the thing that decides
  whether VP9 can be the default rather than merely available.
- AV1 is absent on this machine in both directions, consistent with it being
  pre-M3. AV1 decode should appear on M3+/iPhone 15 Pro+; AV1 *encode* is
  expected to stay absent on every Apple device.

### iOS WKWebView matches desktop Safari

iPhone on iOS 18.7 (A15), reached through `ios_webkit_debug_proxy` into the
**Voxt MAUI app's own WKWebView** — so this is the shipping surface, not mobile
Safari. `isConfigSupported` at 640×360:

| codec | decode | encode |
|---|---|---|
| H.264 | yes | yes |
| HEVC | yes | yes |
| **VP8** | **yes** | **yes** |
| **VP9** | **yes** | **yes** |
| AV1 | no | no |

Real `configure()` + `encode()`, 60 frames paced at 30 fps:

| codec | first output after | frames in → out |
|---|---|---|
| H.264 | 2 frames | 60 → 60 |
| HEVC | 2 frames | 60 → 60 |
| **VP8** | **1 frame** | 60 → 60 |
| **VP9** | **1 frame** | 60 → 60 |

No drops and no errors on any codec.

### macOS WKWebView too — the suspected Catalyst gap isn't there

Probed inside a real `WKWebView` on the Mac mini (`tmp/wkprobe.swift`, a ~40-line
AppKit harness using `callAsyncJavaScript`), because Mac Catalyst embeds the
same system WebKit:

| codec | decode | encode |
|---|---|---|
| H.264 | yes | yes |
| HEVC | yes | yes |
| **VP8** | **yes** | **yes** |
| **VP9** | **yes** | **yes** |
| AV1 | no | no |

Confirmed by real `configure()` + `encode()`: H.264, VP8 and VP9 each took 30
frames in and produced 30 out, no drops and no errors.

Caveat worth stating plainly: this is an AppKit-hosted `WKWebView`, not literally
a Catalyst (UIKit-on-macOS) process. They link the same `WebKit.framework` and
the same media stack — the difference is the UI framework, not codec
availability — but it is a very close proxy rather than the thing itself.

**So VP9 is present on every surface measured: Chromium, Firefox, desktop
Safari, iOS WKWebView and macOS WKWebView.** No Apple surface lacks it, which
means "Closing the VP9 gap" below may have nothing to close — the WASM decoder
is now insurance for old clients, not a Catalyst workaround.

### The conclusion this forces

**VP9 encodes and decodes at real-time latency on every engine we ship to** —
Chromium, Firefox, desktop Safari and iOS WKWebView. There *is* a universal
codec, and it is not H.264.

That reorders this whole plan:

- **VP9 becomes the default**, not a Firefox escape hatch. A call where every
  member speaks VP9 needs no per-client divergence at all.
- **H.264 becomes the legacy fallback** — for clients too old to report a VP9
  capability, and for the Linux/Firefox decode gap.
- The **coverage algorithm below stays**, but as a safety net for mixed-version
  calls and odd devices rather than the everyday path. It is what keeps one
  member's missing codec from disabling another's camera; it is no longer
  expected to fire routinely.
- **AV1 stays low priority.** Absent in both directions on A15 and on the
  pre-M3 Mac mini, so it buys nothing on Apple today and VP9 already covers
  everyone.

What is still unmeasured, and is now the *only* thing between VP9 and being the
default: **sustained CPU at 720p on Apple software VP9.** The 360p run above
kept up with no drops, but wall-clock was dominated by the 33 ms pacing, so it
is a latency and drop test, not a throughput one.

### Chrome/Windows baseline

All four H.264 profiles probe true *and* really encode;
`MediaStreamTrackProcessor` and `MediaStreamTrackGenerator` both present.

### Firefox capture path

`MediaStreamTrackProcessor`, `MediaStreamTrackGenerator` and
`VideoTrackGenerator` are all `undefined`; `requestVideoFrameCallback` exists
but fires only a handful of times a minute for the hidden source `<video>`.
Both are already handled on `fix/h264-codec-selection`.

## Decisions

Taken by Alex, 2026-08-30:

1. **Firefox: drop H.264 from the encode set entirely.** Not kept as a
   last-ranked rung — 570 ms is not worth shipping. H.264 stays in Firefox's
   **decode** set, so a Firefox user can still watch an H.264 sender.
2. **Enable VP9.** **Re-enable AV1**, ranked *below* VP9 until its software
   encode cost is measured. (Since taken further by measurement: VP9 works
   everywhere, so it should be the *default*, not merely enabled — see
   "The conclusion this forces".)
3. **Split encode-side from decode-side exclusion.** Excluding H.264 for
   *encode* must be allowed. Decode-side exclusion is *desirable but not
   mandatory* — a weight the server balances, never a veto.
4. **The server assigns each client its encode codec.**
5. **Retire the old methods via `[LegacyName]`** if the wire change is
   significant.
6. **Probe H.264 decode for real** rather than assuming it, using a basic
   config and an actual decode.
7. **VP9 is the mandatory floor.** The server never excludes it, on either
   side, for any reason. A client that cannot decode VP9 is not a reason to
   renegotiate the call — it is a client that needs a VP9 decoder, and the
   answer is to give it one (see "Closing the VP9 gap" below).
8. **H.264 loses its special status entirely.** It becomes one codec among
   several: excludable on both the encode and the decode side, with no
   "universal fallback" guard anywhere.

Note what 7 and 8 do together: the "never exclude" role moves from H.264 to
VP9. That is not a swap of one assumption for another — H.264 held that role by
assertion (`// always assumed supported`), while VP9 earns it by measurement on
all four engines.

## Design

### Clients report two sets, with usability flags

"Supported" is not a rich enough predicate — Firefox H.264 is supported and
unusable. Each entry carries how it behaves, not just whether it exists:

```
decode: [{ codec, hardware }]
encode: [{ codec, hardware, realtime }]   // realtime: first output within ~2 frames
```

Firefox then reports honestly:

```
decode: [h264, vp8, vp9, av1]
encode: [vp9, av1, vp8]        // no h264
```

The asymmetry is the point: Firefox must still decode H.264 to watch an
iPhone even though it can never send it.

The client also supplies its **preference order** for encoding. The client
knows its own hardware; the server just applies the order it was given. That
keeps device policy (iOS prefers AV1 → H.264 and never HEVC; desktop prefers
HEVC) next to the probing code rather than duplicated server-side.

### The server assigns — by coverage, not by intersection

A hard intersection `D = ∩ decode_c` is wrong, and the Linux/Firefox case shows
why: one member who cannot decode H.264 removes H.264 from the whole call, so
an iPhone — which can only encode H.264 or HEVC — is left with an empty encode
set and goes receive-only. **One Linux user silently disables every Apple
camera in the call.** That trade is never worth making automatically.

The fix is to admit **partial reachability**. A receiver simply does not see a
sender whose codec it cannot decode; that is a much better outcome than nobody
seeing the iPhone at all. So the server optimises coverage instead of
intersecting:

> Receiver `r` sees sender `s` iff `k_s ∈ decode_r`.
> For each sender `s`, choose `k_s ∈ encode_s` maximising the number of members
> that can decode it.

This **decomposes per sender** — who can see `s` depends only on `k_s` — so it
is a small independent choice per client, not a joint optimisation. With a
handful of codecs it is a loop, not a solver.

Ordering of criteria, highest first:

1. **Coverage** — how many members can decode it (weighted; see below).
2. **Codec reuse** — prefer a codec already assigned to another sender. N
   distinct codecs in a call means every receiver runs N decoders of different
   families, which exceeds hardware decoder sessions on mobile. This is a
   tie-breaker, *not* a hard rule: it must never cost coverage.
3. **The sender's own preference order** — efficiency and power, as reported by
   the client.

**VP9 is the floor, which makes the search total.** Because every client is
required to decode VP9 (decision 7), VP9 always scores 100% coverage, so there
is always a valid answer and the algorithm never has to choose between "someone
is invisible" and "nobody sends". Every other codec is an *optimisation*: it is
picked only when it covers everyone too and ranks higher in the sender's
preference order — HEVC or H.264 between Apple devices, AV1 where it is cheap.
Divergence and partial coverage remain possible only for mixed-version calls
where an old client reports no VP9 at all.

`encode_s = ∅` ⇒ that client is **receive-only**, reported explicitly so the UI
can explain it. A client that is merely invisible *to some members* is a
different, softer state and should be reported separately — "2 of 5 members
cannot see you" is actionable; a silent black tile is not.

Codec **upgrades** keep the existing `CodecSwitchHysteresisWindow` (10 s);
downgrades stay immediate.

### Two kinds of exclusion, deliberately different

- **VP9 is exempt from both.** It is the floor; nothing may remove it. This
  replaces the old `if (category === 'h264') return` guards, which protected
  the wrong codec for the wrong reason.
- **Encode exclusion** — allowed for every other codec including H.264, and
  needs no server logic: the client omits it from `encode`, so the server can
  never assign it. "Exclude H.264 only if something else remains" falls out for
  free — VP9 always remains.
- **Decode exclusion** — a *preference*, never a veto. Under the coverage model
  above, a client dropping H.264 from `decode_c` no longer forces anything on
  anyone: it lowers H.264's coverage score by one member, which the server
  weighs against the alternatives. If H.264 still reaches more members than
  anything else the iPhone can send, the iPhone keeps sending H.264 and the
  excluding client simply does not see it.

  This is what "desirable but not mandatory" means mechanically: exclusion
  moves a weight, it does not remove an option. The weight can be tuned —
  a member who *cannot* decode a codec should count more heavily than one who
  can but would rather not (a slow software path), which is the same
  cost/efficiency axis the preference order already expresses.

Today one guard (`if (category === 'h264') return`) covers both. Splitting it
is the smallest independent piece of this work and can land first.

### Wire change

`ILiveVideoStreams` (`src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`):

```csharp
Task RegisterMember(
    Session session, ChatId chatId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken);

[ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
Task<ApiArray<string>> GetSupportedCodecs(Session session, ChatId chatId, CancellationToken cancellationToken);
```

Both change shape: `RegisterMember` grows a second set plus per-entry flags,
and `GetSupportedCodecs` becomes a per-client *assignment* rather than a list.
That is significant enough to retire them, following the `IChats.GetNews`
pattern (`src/dotnet/Api.Contracts/Chat/IChats.cs:19-30`):

- New methods take the new shapes under new names.
- Old names keep working for old clients via
  `[LegacyName(nameof(New), "<version>")]`, mapping a single decode list to
  `decode` and deriving `encode` from it exactly as today — so pre-upgrade
  clients degrade to current behaviour rather than breaking.
- Mark the old methods `[Obsolete]` with the date and the reason.

## Closing the VP9 gap

Making VP9 mandatory means some client, somewhere, will not have it. Measurement
has since narrowed that to **old builds only** — every current surface has VP9,
including the macOS WebView that was the suspected gap — so this is contingency,
not a prerequisite, and should be built only if telemetry shows clients that
need it. The answer is still to ship such a client a decoder rather than
renegotiate the call, and for VP9 that is markedly easier than the H.264 attempt
this plan supersedes:

- **No patent problem.** VP9 is royalty-free under the Open Media licence.
  Every objection that killed the libav H.264 fallback — the AVC pool, Cisco's
  binary-only royalty umbrella, becoming a codec-unit distributor — simply does
  not apply.
- **No custom build.** libav.js publishes prebuilt `webm` and `vp8-opus`
  variants covering VP8/VP9; `D:\Projects\libav.js\dist` already has
  `libav-6.10.9.0-webm.wasm.wasm` at ~2.3 MB, about half the H.264 build.
- **Decode only**, which is the cheap direction and needs no openh264.

The `LibavVideoDecoder`-behind-`DecoderLike` shape from the retired plan applies
unchanged; only the variant and the licensing conclusion differ.

## Implementation order

1. **Split encode/decode exclusion** (`codec-support.ts`) — independent, small,
   unblocks everything else. Already half-done on
   `fix/h264-codec-selection` via `excludeEncoderCodecString`.
1b. **Probe H.264 decode for real** — see below. Today
   `detectSupportedDecoderCodecs()` hard-codes `['h264']` and never probes it,
   which is precisely the lie that makes a Linux/Firefox client claim a codec
   it cannot play.
2. **Client capability probing**: produce the two sets with `hardware` and
   `realtime` flags. `realtime` is measured the way the numbers above were —
   submit frames until the first output, cache per codec per session.
   Firefox H.264 fails this and drops out of `encode` on its own, which is
   decision 1 implemented as a *measurement* rather than a browser check.
3. **Enable VP9 and AV1** in `REPRESENTATIVE_CODECS` with AV1 below VP9.
4. **Wire + server**: new methods, `[LegacyName]` aliases, the assignment
   algorithm in `LiveVideoBackend`.
5. **Client consumes the assignment** instead of picking locally.
6. **Receive-only state** surfaced in the UI.

Step 2 is worth doing carefully: a measured `realtime` flag means we never
again hard-code a browser quirk that a future release fixes.

## Probing H.264 decode honestly

`detectSupportedDecoderCodecs()` currently starts from
`const codecs: string[] = ['h264']; // always assumed supported`. That
assumption is the root of the Linux/Firefox problem: the client advertises a
codec it may not have.

Two properties the replacement needs:

- **Probe the floor, not the ceiling.** Ask about the most basic thing that
  would still be useful — Constrained Baseline at a small size, e.g.
  `avc1.42E01E` at 320×240 — under both `prefer-hardware` and `no-preference`.
  A device that fails *that* has no H.264 decoder at all. Probing High 4.0 at
  1080p answers a different and much narrower question.
- **Do not trust `isConfigSupported` alone.** On Linux it is exactly the call
  that lies ([bug 1918769](https://bugzilla.mozilla.org/show_bug.cgi?id=1918769):
  reports supported, then `configure()` throws). The decisive test is to
  **decode one frame**: embed a tiny Annex B IDR (a 16×16 grey keyframe is a
  couple of hundred bytes), feed it to a real `VideoDecoder`, and require a
  `VideoFrame` back. That needs no camera, no permissions and no network, runs
  in a few milliseconds, and is the only check that cannot be fooled.

Cache the result per session. The same "decode one real frame" harness gives
the `hardware` flag substance for every other codec too.

Encode is unaffected by this probe — Firefox is excluded from H.264 *encode*
on the `realtime` measurement regardless of what its decoder can do.

## Open items

1. **VP9 software-encode CPU on Apple**, sustained at 720p30 with the pacing
   removed. The one number left between VP9 and being the default codec.
2. **AV1 on an M3+/iPhone 15 Pro+.** Both machines measured here predate AV1
   hardware, so "AV1 decode: no" is a property of these devices, not of Apple
   in 2026. Worth one probe on newer hardware before AV1 is ranked.
3. **Does any Apple device encode AV1?** Not on the Mac mini (above), and
   near-certainly nowhere — no Apple silicon has an AV1 encoder and Safari
   ships no software one. Confirm on the iPhone before letting policy depend
   on it.
4. **AV1 software encode CPU cost.** Firefox emits AV1 on frame 1, but that
   measured *latency*, not throughput. Measure sustained 720p30 before AV1
   outranks VP9 anywhere.
5. **HEVC in the new model.** Currently probed and preferred on Apple; it needs
   an entry in both sets and a place in each platform's preference order.

## Tooling

- `tmp/ff-bidi.mjs` — WebDriver BiDi driver for the host Firefox. Firefox
  dropped CDP, so chrome-devtools MCP cannot reach it. Commands:
  `contexts | nav <url> | reload | eval <file> [urlSubstr] | click <sel> | shot <file> | perm | watch`.
  Start Firefox with `--remote-debugging-port=9333 --remote-allow-hosts=localhost,127.0.0.1`,
  fully quitting it first (a running instance ignores the flag). Only one BiDi
  session exists at a time and a killed client orphans it until Firefox
  restarts, so never `timeout`/SIGKILL the driver.
- Cache-busting after a `server-loop` rebundle: the bundle URL is unchanged and
  `immutable`, and **worker bundles are fetched by `new Worker(url)`, so a plain
  reload keeps serving the stale worker**. `tmp/ff-bust3.js` refetches
  `videoRecorderWorker` / `videoPlayerWorker` via `Versioning.mapPath` with
  `cache: 'reload'` before reloading. Firefox BiDi has no `ignoreCache`.
- Enable pipeline logging in the page with `logLevels.override('*Video*', 1)` —
  editing the `logLevels` localStorage key directly does **not** work.
- `~/bin/safari` on the Mac mini drives real desktop Safari over
  safaridriver + BiDi (`new | go | eval [-f] | shot | watch | end`).
  **Two traps, both cost an hour to find:**
  - Under `script.evaluate` with `awaitPromise`, Safari runs neither timers nor
    WebCodecs promise resolution — `setTimeout` never fires and a single
    `isConfigSupported` never settles, so the eval returns `null` with no
    error. The page runs normally *between* BiDi calls, so the pattern that
    works is: one **synchronous** eval that kicks off the async work and stashes
    the result on `globalThis`, then a second eval a few seconds later that
    reads it. `tmp/t10.js` / `tmp/t11.js` are working examples.
  - A fresh session sits on `about:blank`. Run `~/bin/safari go https://…`
    first or WebCodecs answers from a non-secure context.
- `tmp/wkprobe.swift` — probes a real macOS `WKWebView` with no app, no
  inspector and no proxy: `scp` it to the Mac and `swift wkprobe.swift`. Note
  `callAsyncJavaScript` takes a **function body**, so the script must `return`;
  passing a bare `(async () => …)()` expression yields `nil` with no error.
- `tmp/ios-eval.mjs` — evaluates JS in the iPhone's WKWebView through
  `ios_webkit_debug_proxy` (already running on the Mac mini, port 9222).
  It speaks the **WebKit Inspector protocol, not CDP**: every command must be
  wrapped in `Target.sendMessageToTarget` and replies arrive via
  `Target.dispatchMessageFromTarget`, addressed to the **frame** target rather
  than the page target. Uses Node 22's built-in `WebSocket` — there is no `ws`
  module on that machine. Same start-then-poll pattern as Safari.
  `node ios-eval.mjs <pageIdx> <file>`; `curl localhost:9222/json` lists pages.
