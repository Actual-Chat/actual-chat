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

Four presets, ordered best-first in the menu:

| Order | Label | Resolution | Re-encode | EXIF |
|---|---|---|---|---|
| 1 | Original (with EXIF) | unchanged | no | kept |
| 2 | **Original resolution** (default) | unchanged | yes | stripped |
| 3 | 4K | long side ≤ 3840 | yes | stripped |
| 4 | 1080p | long side ≤ 1920 | yes | stripped |

- The effective output size is always `min(source, cap)` per axis. A 3000 px photo at
  the 4K preset encodes at 3000 px; no preset ever upscales.
- The 8K rule from the first spec stands: a source whose long side exceeds 7680 px is
  re-encoded to 7680 even under the Original presets, so the server's pixel bound can
  stay.
- `Original (with EXIF)` is the only preset that passes bytes through untouched, and
  the only one that sets `KeepMetadata` on the upload.
- The default moves from 4K to `Original resolution`: recipients get the full frame,
  and the sender still pays less than the raw camera file because jpegli at
  distance 1.9 beats a phone's own encoder.

The menu is ordered highest quality at the top. Localization gains one key for the new
`Original resolution` entry; the existing `Original`/`Original with EXIF` keys are
reused and relabelled where the wording changes.

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
forest differ several-fold at the same resolution. The model, its constants and its
measured error come from a study over real phone photos, fitted on one month's photos
and validated on another (`tmp/size-model/REPORT.md`). Two cases carry their own rule:

- **No resize** (source below the cap, and always for the Original presets) — a recode
  of already-compressed bytes, which predicts differently from a downscale.
- **HEIC sources**, whose bytes-per-pixel is far lower than JPEG's at equal quality, so
  the complexity proxy needs a correction factor.

Estimates are labelled as approximate in the UI. If the validated error is too large to
be honest about, the sizes come out of the menu rather than shipping a number that
misleads.

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
- **The default preset sends more bytes than before.** `Original resolution` replaces
  4K as the default, so a 12 MP photo uploads at 12 MP. Recipients decode a larger
  image; the sender's own bandwidth bill goes up. The quality menu is where anyone who
  cares opts down, and the estimates are what makes that choice informed.
- **Placeholders make every chat tile read slightly heavier** — see the cost note
  above. The alternative, a 28-byte BlurHash, was measured and rejected: at this budget
  the extra bytes buy recognizable shapes instead of an abstract gradient.
