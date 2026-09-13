# Image presets, deferred upload and inline placeholders

**Status:** approved design, 2026-09-12
**Branch:** `feat/media-quality-selector` (continues the work specified in
[2026-09-11-client-image-pipeline-design.md](2026-09-11-client-image-pipeline-design.md))

This spec revises three things the first pipeline shipped — the preset list, when
uploads start, and what an image tile shows before its bytes arrive — and fixes two
defects found by device testing.

## Why

Device testing of the first pipeline surfaced the problems this design answers:

- A HEIC picked on Android shows a stuck "loading" tile forever. The preview URL points
  at the source file, and Chromium cannot decode HEIC. It never recovers, because
  nothing updates the preview after processing produces a JPEG.
- Uploading the moment processing ends means every preset change re-uploads the whole
  file, and every discarded upload session leaves an orphan media row behind.
- The quality menu's byte sizes are produced by encoding the image at each preset. That
  is a lot of work for a number in a menu.
- A recipient sees a grey pulsing box until the image arrives over the network, even
  though the sender could have told them what the photo looks like in a few hundred
  bytes.

## Presets

Five presets, ordered best-first in the menu:

| Order | Label | Pixel budget | Long-side cap | Re-encode | EXIF |
|---|---|---|---|---|---|
| 1 | Original (with EXIF) | — | — | no | kept |
| 2 | Original | — | — | no | stripped |
| 3 | Up to 50mpx / 12K | 50.3 MP | 12288 | yes | stripped |
| 4 | **Up to 12mpx / 6K** (default) | 12.6 MP | 6144 | yes | stripped |
| 5 | Up to 3mpx / 3K | 2.8 MP | 2880 | yes | stripped |

The label states both limits — the pixel budget and the longest side it permits — since
either can be the one that binds. The figures are rounded for the menu; the exact
budgets are the ones in the table.

One rule generates every row: the budget is the area of a 4:3 box at the nominal size,
`L² × ¾` for L = 8192, 4096, 1920, and the long side may reach `1.5 × L`. So a wide
photo spends its budget on width instead of being punished for its shape — the top
preset silently allows 12288 px of length — and only extreme panoramas are clipped by
the long-side cap. Nothing is ever upscaled: an image already inside both limits is
re-encoded at its own size.

The budgets are chosen so the common case does not resize at all:

- **The 12mpx preset passes a phone's main sensor through untouched** — 4032×3024 on
  iPhone, 4000×3000 on Samsung. A 3840-based cap would have resized both by ~1% for no
  reason, which is why the nominal is 4096.
- **The 50mpx preset** covers 48 and 50 MP sensors without resizing, and stays far
  inside what the encoder can do: jpegli was measured encoding up to 240 MP before
  failing, so 50 MP has roughly 5× margin.

  Memory, not the encoder, is the limit on a phone. jpegli needs ~8.8 bytes of wasm
  heap per input pixel — ~440 MB at 50 MP — on top of the decoded bitmap and the canvas
  copy, all inside one process budget. Desktop has room; a phone may not, and it does
  not fail politely: iOS kills the app and Android kills the WebView renderer, taking
  the draft with it. So the client checks the target pixel count **before** decoding and
  declines the preset on a device that cannot afford it, leaving the user to pick a
  smaller one. Huge photos are a desktop activity; the product's answer on a phone is a
  smaller preset, not a lost draft.
- **The 3mpx preset** yields 1920×1440 for a 4:3 photo.

Both Original presets pass the bytes through untouched; they differ only in metadata —
`Original (with EXIF)` is the one that sets `KeepMetadata` on the upload, while `Original`
has the server strip it. Neither ever re-encodes. The 7680 px rule from the first spec
is gone, replaced by these budgets.

### Sources too large for the server's image bounds

The server bounds a stored image at 12288 px per side and 96 MP total. A 200 MP phone
shot sent as `Original (with EXIF)` exceeds that, and rejecting it after the user
pressed Send is the worst possible outcome.

Such an upload is stored as a **file attachment** instead — the exact bytes, which is
what Original promises, on the path `AttachmentImageUploadProcessor` already uses for
images it cannot identify. The recipient sees a download card rather than an inline
image: no preview, no placeholder, no dimensions. That is a real difference, so the
menu says so on the entry itself rather than letting it surprise the sender. Every
other preset re-encodes such a source down and stays a normal image.

The menu is ordered highest quality at the top. Localization gains keys for the three
new labels; the existing `Original with EXIF` key is reused.

### Formats that are never re-encoded

GIF cannot be re-encoded without destroying its animation, so it passes through at any
preset — as do animated WebP and APNG. The preset menu has nothing to offer for these
and should not imply otherwise.

They still get a placeholder: `createImageBitmap` on an animated source decodes its
first frame, which encodes exactly like any other photo. A still image that the client
cannot decode at all gets no placeholder, and the recipient falls back to the grey
skeleton.

## Upload timing

Attaching a photo no longer uploads anything. A draft is **uncommitted** until the
first of:

- the user types in the message editor,
- the user picks a preset,
- the user presses Send.

At that moment the draft **commits**: every attachment it holds encodes and uploads,
and any attachment added later to the same draft encodes and uploads immediately.
Send waits for whatever is still in flight. Posting the message returns the editor to
uncommitted.

The window is deliberately short. A user who attaches and immediately sends pays the
encode at Send; the far more common attach-then-write-a-caption case overlaps encoding
and upload with typing, which is where the original design's speed came from. What it
buys is that attaching a photo, looking at it, and removing it costs nothing —
no bytes uploaded, no media row created, no server work.

Two things still happen at attach time, because the tile is on screen immediately:

1. **Placeholder generation** (see below).
2. **HEIC/AVIF conversion.** Formats the WebView may not decode are converted to JPEG
   right away, and the attachment's preview is repointed at the converted file. This is
   what fixes the stuck Android tile. Every other format keeps the source preview and
   converts nothing until the draft commits.

## Size estimates

The menu shows an expected upload size per preset. These are **computed, never
encoded**: encoding an image three times to label a menu is work the user pays for in
battery and latency for no result they keep.

The estimate is a function of what is already known without decoding — the source's
pixel dimensions and its file size:

```
estimatedBytes = f(targetPixels, sourceBytesPerPixel)
```

The source's bytes-per-pixel is a free proxy for how busy the photo is: a wall and a
forest differ several-fold at the same resolution. The constants come from a study over
1200 real photos spanning four cameras, fitted on 41 and validated on 25 the fit never
saw (`tmp/size-model/`, data and scripts). Three cases carry their own rule:

- **Resize + re-encode** — the main path.
- **No resize** — a recode of already-compressed bytes, which predicts differently from
  a downscale, and whose ratio turns out to be a property of the source's own encoder
  rather than a universal function.
- **Sources with no usable bitrate signal** — PNG, screenshots, anything lossless — fall
  back to a pixels-only model, since their byte size reflects the encoder, not the
  content. A per-format multiplier corrects HEIC, WebP and AVIF sources, whose
  bytes-per-pixel is far lower than JPEG's at equal quality.

**The estimates are coarse on purpose.** Holdout error is ~20% median overall but ~34%
median on phone photos with a p90 near 160%, the worst cases being smooth hazy scenes
where a phone's computational pipeline spends bytes that do not survive a resize. A
number like "2.14 MB" would imply a precision the model does not have. So sizes are
shown rounded — to 0.1 MB below 1 MB, and progressively coarser above it, up to whole
megabytes — and prefixed to read as approximate. The menu's job is to let someone tell
2 MB from 12 MB, not to predict the byte count.

## Inline placeholders

Every image attachment carries a tiny version of itself, generated on the client and
stored with the media, so a recipient's tile paints the photo's shapes and colors
instantly instead of a grey pulse.

**Encoding.** jpegli at distance 6, 4:2:0, long side exactly 64 px, aspect preserved
with the effective ratio clamped to 2:1 so panoramas and tall crops do not degenerate
into a line.

The obvious choice was WebP, which wins on whole-file size. It loses once headers are
stripped, which this container does anyway: measured over 15 real photos as stored
bytes, jpegli is **413 bytes median against WebP's 414**, and its worst case is
**603 against 776** — better on the number a hard ceiling actually cares about. Since
jpegli already ships in this app, choosing it removes a second WASM codec, its binary,
and the WebKit trap where canvas silently encodes PNG when asked for WebP.

The one thing WebP does better is flat regions: jpegli shows 8×8 block edges on sky
and other smooth areas at this size, at every distance tested. The placeholder is
painted blurred and upscaled behind the real image, which hides the blocking entirely,
so this costs nothing in practice — but a future change that paints it sharp would
need to revisit the choice.

**Container.** The stored blob is not a JPEG file. It is:

| Byte | Meaning |
|---|---|
| 0 | Format mark. `1` = header-stripped jpegli, distance 6, 4:2:0, long side 64. Other values are reserved, so the encoding can change later without touching stored rows — the decoder branches on this byte. |
| 1 | The side that is not 64, as a signed value: positive = horizontal (width = value, height = 64), negative = vertical (height = \|value\|, width = 64). `64` means square. |
| 2.. | The JPEG with its reconstructable 236-byte prefix removed: SOI, DQT, the SOF skeleton, the SOS header and EOI, all of which the decoder rebuilds from fixed bytes plus byte 1's dimensions. The Huffman tables stay inline — the encoder optimizes them per image, and forcing standard tables to drop them costs ~26% more than it saves. |

The long side is always encoded at exactly 64 px, upscaling sources smaller than that,
so the container never needs to express a second dimension. Byte 1 also lets the
renderer reserve the right aspect ratio before decoding anything.

**Storage.** Base64 in the media row's metadata bag, set at reservation time alongside
the dimensions the client already sends. The bag is an opaque JSON column, so this
needs no migration, no new DTO field and no serializer changes; the bag's schema
accepts primitives only, hence base64 rather than raw bytes. Media rows without the key
— every row that predates this feature — fall back to today's behaviour.

**Cost.** Media rows are eagerly joined on every chat tile read, so a placeholder is
paid on every scrollback page, not once per send: ~550 bytes as base64 per image
(~800 at the measured worst case), roughly 5.5 KB for a page of ten images. This is
the reason for the 64 px ceiling, and
the reason a raster was chosen over a separate blob row — a second row plus a blob plus
a join for under a kilobyte would cost more than it saves.

**Rendering.** The placeholder becomes the first layer in the `image-skeleton`
component, behind the existing proxy thumbnail and the full image. Because it travels
inside the media row, the message list, the media gallery and the full-screen viewer
all get it from one change. The existing grey pulse remains the fallback when a media
row has no placeholder.

## A poisoned encoder is never rebuilt

Measuring the encoder's ceiling turned up a defect in the shipped pipeline. jpegli has
two distinct failure modes: past ~245 MP it throws a `WebAssembly.RuntimeError` from
inside the wasm call and **the instance is poisoned** — every later encode on it traps
too, reproducibly — while past ~255 MP it merely returns null and the instance stays
perfectly healthy. A fresh instance recovers completely in the first case.

`getEncoder()` in `image-processor-worker.ts` caches the encoder indefinitely and never
invalidates it. So a single trap would silently downgrade every subsequent image to the
canvas encoder for the life of the page — worse output, no error, nothing a user or a
log would show. The encoder is discarded and rebuilt on `WebAssembly.RuntimeError`
specifically, and kept on a plain `Error`, which is the failure that leaves it usable.

The new pixel budgets keep the client far from that band, so this is belt and braces —
but it is cheap, and the current behaviour fails invisibly, which is the worst way to
fail.

## Orphan media rows

Discarding an upload session removes its reserved media. Today a session reserves a
media row before the first byte is uploaded and never removes it when the session is
thrown away, so every discarded session leaves a contentless row and its progress row
behind permanently — there is no server-side collection for them. The upload's bytes
are already deleted correctly; only the rows leak.

Deferred upload makes this far rarer, since an attachment removed before the draft
commits never reserves anything. A preset change after commit still discards a session,
so the fix belongs here regardless.

## Out of scope

- **Video**: no client-side transcoding, and no client-generated video placeholders.
  Transcoding feasibility is researched separately in
  `docs/research/2026-09-12-client-video-transcoding.md`; videos keep the server-made
  thumbnail they have today.
- Backfilling placeholders for existing media.
- Display P3 preservation, the iOS share extension, and the multi-chat share path —
  all still as recorded in the first spec.

## Consequences worth stating

- **Send is slower for the attach-and-send-immediately user**, by roughly the encode
  time of their photos. This is the cost of not uploading what may never be sent.
- **The default preset sends more bytes than before.** The default is still the 4K-class
  option, but its budget grew from a 3840 long side to 12.6 MP, so a phone photo uploads at its
  full 12 MP instead of being downscaled. Recipients decode a larger image, and the
  sender's bandwidth bill goes up — bought deliberately, so the common case is never
  resized. The quality menu is where anyone who cares opts down, and the estimates are
  what makes that choice informed.
- **Placeholders make every chat tile read slightly heavier** — see the cost note
  above. The alternative, a 28-byte BlurHash, was measured and rejected: at this budget
  the extra bytes buy recognizable shapes instead of an abstract gradient.

## Implementation notes

Recorded after all thirteen implementation tasks landed, where the shipped behaviour
differs from or sharpens what is written above.

**The preset enum's declaration order is not the menu order.** `ImageQualityPreset` is
`Mpx12 = 0, Mpx50, Mpx3, Original, OriginalWithExif` — `Mpx12` is first only so it is
the type's default value. The menu order given in the table above (EXIF, Original, 50,
12, 3) lives entirely in the quality selector's `Presets` array; nothing about the enum
declaration reflects it.

**`ImageOutputSpec.Original` passes `MaxPassthroughPixels: null`**, not the server's
pixel bound (Ruling P1). This is what makes both Original presets impossible for the
mobile encode guard to decline — passthrough for them is unconditional, and it is the
server, not the client, that decides whether an oversize source becomes a file
attachment.

**"Sent as a file" fires on three arms, not two** (Rulings P12/P13): whenever the
source's bytes are uploaded unchanged AND the source exceeds the server's bound
(12288 px / 96 MP). That covers both Original presets and a resize preset the mobile
encode guard declined — a decline also passes the source through unchanged. One shared
`ExceedsServerBounds` predicate covers all three.

**Once a preset is applied and the attachment is idle (not processing), its menu row
shows the real `attachment.Length`**, not the estimate (Ruling P14) — including a
declined or failed-encode row — so the row the user is about to send always matches
what will actually go out.

**The placeholder container's numbers above are superseded.** The design originally
called for a 236-byte reconstructable prefix; what shipped strips 220 leading bytes
(SOI + DQT + SOF0, jpegli's real Huffman-optimized encoding, not a synthetic one) plus
a trailing `FF D9` (EOI) appended separately. Byte 0 is the format mark; byte 1 is the
signed short side (positive = horizontal, `64` = square); the long side is always
exactly 64. Format 1 patches the decoded width/height into the fixed prefix at byte
offsets 206 and 208 (big-endian). **Changing any of those prefix bytes requires a new
format mark** — stored rows carry the old prefix's shape, and a decoder that assumed
the new one would misdecode every placeholder already written.

**jpegli's measured ceiling is 240 MP.** Between roughly 245 and 250 MP the encoder
throws `WebAssembly.RuntimeError` from inside the wasm call and *poisons the instance* —
every later encode on that same instance traps too, until it is discarded and rebuilt.
At 255 MP and above it instead returns null cleanly, leaving the instance healthy. The
mobile encode guard checks the *target* pixel count before decoding and declines a
preset a device cannot afford, rather than attempting the encode and hitting the crash;
its 16 MP threshold is a judgement call, not a measured one, settled by the device
matrix rather than by this document.

**Android's Chromium silently caps decode at roughly 75% linear scale for very large
sources** — a ~200 MP source comes back from `createImageBitmap` at ~112 MP regardless
of what was requested, with no error. No preset can recode such a photo at full
resolution on that platform; an Original preset (passthrough) is the only way to
preserve it there.

Other places the implementation differed from this design:

- **Placeholder generation happens at draft commit, not at attach time** (Ruling P10),
  unlike the "two things still happen at attach time" list above. It is only needed if
  the message is actually sent, and generating it at attach would spend work on
  attachments that get removed before commit — the opposite of what deferring upload is
  for.
- **The HEIC/HEIF size-estimate multiplier shipped at 2.0**, arrived at by measuring six
  real iPhone HEIC photos recoded end-to-end through jpegli at the default budget
  (20.0% median / 80.9% worst error, near-unbiased). Data and scripts are in
  `tmp/heic-multiplier/` for a future re-fit.
- **Animated WebP and APNG still show the quality-preset chip**, even though — like
  GIF — they are never re-encoded (Ruling P11). C# cannot tell an animated WebP or APNG
  from a still one by content type alone, and surfacing the worker's own answer back to
  the chip was judged worse: the chip would appear, offer a preset, then have it vanish
  after commit. The never-re-encoded invariant holds regardless, at the worker. Proper
  fix, not yet scheduled: an attach-time header sniff (APNG `acTL` before `IDAT`; WebP
  `VP8X` flag).
- **The media gallery grid does not get placeholders** (Ruling P9). `VisualMediaItem`,
  read by `VisualMediaList`, is a denormalized projection written by
  `ChatMediaIndexingFlow` with no `Media`/`Placeholder` field; filling it needs a new
  column on `DbChatVisualMediaItem` plus an indexing-flow change, and is its own task.
  The full-screen viewer already gets placeholders, via `ChatEntryAttachment.Media`.

Deferred, not attempted, with reasons recorded in the execution ledger: tiling for very
large images (measured slower than one scaled decode — cost scales with tile count
since each `createImageBitmap` crop re-decodes the whole file), and backfilling
placeholders for media that predates this feature (old rows return `""` and fall back
to the grey skeleton, same as before).
