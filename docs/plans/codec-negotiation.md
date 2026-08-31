---
title: Codec negotiation — per-client encode assignment
description: Clients report decode and encode capabilities separately; the server assigns each client an encode codec by how many members can decode it, so one member's missing codec never disables another's camera.
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
   encode cost is measured.
3. **Split encode-side from decode-side exclusion.** Excluding H.264 for
   *encode* must be allowed. Decode-side exclusion is *desirable but not
   mandatory* — a weight the server balances, never a veto.
6. **Probe H.264 decode for real** rather than assuming it, using a basic
   config and an actual decode.
4. **The server assigns each client its encode codec.**
5. **Retire the old methods via `[LegacyName]`** if the wire change is
   significant.

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

`encode_s = ∅` ⇒ that client is **receive-only**, reported explicitly so the UI
can explain it. A client that is merely invisible *to some members* is a
different, softer state and should be reported separately — "2 of 5 members
cannot see you" is actionable; a silent black tile is not.

Codec **upgrades** keep the existing `CodecSwitchHysteresisWindow` (10 s);
downgrades stay immediate.

### Two kinds of exclusion, deliberately different

- **Encode exclusion** — allowed for any codec including H.264, and needs no
  server logic: the client omits it from `encode`, so the server can never
  assign it. "Exclude H.264 only if something else remains" falls out for free
  — if nothing remains, `E_c` is empty and the client is receive-only.
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

1. **Safari/WebKit VP8, VP9 and AV1 support — still unknown, and it gates
   step 3.** If WebKit cannot *decode* VP9, then `D` excludes VP9 for any call
   containing an Apple member, and Firefox's only route to those members is
   AV1 (iPhone 15 Pro+ / M3+) — otherwise Firefox is receive-only there.
   `tmp/codec-caps.js` is written and ready; `~/bin/safari eval -f` on the Mac
   mini returned `null` twice, so the harness needs debugging first (its own
   header notes Safari's BiDi drops exception details, so the page must hand
   errors back as data — the probe already does that, so the failure is
   elsewhere). Also run it against the tethered iPhone.
2. **Does any Apple device encode AV1?** Near-certainly not — no Apple silicon
   has an AV1 encoder and Safari ships no software one. Confirm with the same
   probe before letting policy depend on it.
3. **AV1 software encode CPU cost.** Firefox emits AV1 on frame 1, but that
   measured *latency*, not throughput. Measure sustained 720p30 before AV1
   outranks VP9 anywhere.
4. **HEVC in the new model.** Currently probed and preferred on Apple; it needs
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
