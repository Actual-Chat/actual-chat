# SVG system icons follow-up (#3680)

## Background

Branch `feat/3680-get-rid-of-patched-svgskia` removes the patched local
`Svg.Skia` build from the **client** and rasterises every **user-uploaded**
icon to PNG so the client never has to render SVG itself.

What is already done on the branch:

- Removed all `lib/nupkg/*Svg.Skia*` / `Svg.Custom` / `ShimSkiaSharp` /
  `Svg.Controls.*` packages and the matching `nuget.config` source.
- `Svg.Skia` (the standard, unpatched NuGet) is now referenced **only on the
  server**, in two places:
  - `src/dotnet/Core.Server/Uploads/IconUploadProcessor.cs` — converts SVG
    to PNG at upload time for any chat / place / avatar picture upload.
  - `src/dotnet/App.Server/Flows/IconSvgToPngMigrationFlow.cs` — background
    flow that walks `DbAvatar` / `DbChat` / `DbPlace`, finds media records
    whose blob still ends with `.svg`, and rewrites them as PNG.
- `IconUI` (Maui) no longer rasterises SVGs locally
  (`src/dotnet/Maui/Services/Icons/IconUI.cs`); the PNG branch in
  `AvatarPicturesController.UploadPicture` and `AvatarFormat.Png` defaulted
  in `IconQuery` mean iOS/Android share extensions only ever ask for PNG
  for generated avatars.
- `PicUpload` UI restricts the file picker to raster types, and
  `IconUploadProcessor` accepts SVG/AVIF/WebP/etc. but always emits PNG.

The user-uploaded path is therefore fully covered: anything entering the
system as SVG is converted to PNG before it lands in the blob store, and
existing rows are migrated by `IconSvgToPngMigrationFlow`.

## The open question

There is still one category of SVGs that the migration flow does **not**
fully cover and that the upload processor never sees: **the SVGs that the
codebase itself bakes in and seeds into the database** through
`MediaDbInitializer`. The example the user called out is the "Notes" chat
icon, but the full set is six files. Because `MediaDbInitializer` re-seeds
those rows from `.svg` resources on every startup, they will keep their
`.svg` blob ID forever.

The scope of this plan is **only** those embedded resources. Generated
Marble/Beam avatars, the Wall-E external dicebear URL, the Maui app icon,
landing page art, the `nodejs/icons` set, etc. are all out of scope.

## Inventory

`src/dotnet/Media.Service/Resources/`

| File            | Seeded as media id      | MediaKind     | Used from |
|-----------------|-------------------------|---------------|-----------|
| `notes.svg`     | `system-icons:notes`    | `ChatPicture` | `ChatsBackend.cs:1683` (auto-creating user "Notes" chat); `CreateChatsStep.razor:130` (onboarding default) |
| `family.svg`    | `system-icons:family`   | `ChatPicture` | `CreateChatsStep.razor:130` (onboarding) |
| `friends.svg`   | `system-icons:friends`  | `ChatPicture` | `CreateChatsStep.razor:130` (onboarding) |
| `coworkers.svg` | `system-icons:coworkers`| `ChatPicture` | `CreateChatsStep.razor:130` (onboarding) |
| `alumni.svg`    | `system-icons:alumni`   | `ChatPicture` | `CreateChatsStep.razor:114` / `:130` (onboarding) |
| `sherlock.svg`  | `system-icons:sherlock` | `UserPicture` | `Constants.Sherlock.MediaId` (AI bot author picture) |

Wiring:

- `src/dotnet/Media.Service/Resources/Resource.cs` — `Resource` class
  exposes each file as a manifest resource stream.
- `src/dotnet/Media.Service/Module/MediaDbInitializer.cs:14-27` — calls
  `MediaUploader.AddMedia(<id>, <Resource>, <MediaKind>)` for each one
  on every fresh DB initialisation; the blob stored in object storage
  keeps the `.svg` extension and `image/svg+xml` content type.

## Goal

After this work, no `system-icons:*` `DbMedia` row resolves to a `.svg`
blob — neither on a brand new DB seeded from scratch, nor on an existing
DB after `MediaDbInitializer` re-runs.

## Approach

**Keep the SVGs in `Media.Service/Resources/` as committed source-of-truth
artwork. Add a polyglot `convert-system-icons.cmd` at the project root
that shells out to `rsvg-convert` to write a sibling `*.png` for each
`*.svg`. Commit both files. Embed only the PNGs as resources.
`MediaDbInitializer` seeds the PNGs.**

Workflow for a developer adding or updating a system icon:

1. Drop the new `.svg` into `src/dotnet/Media.Service/Resources/`
   (or edit an existing one).
2. Run `./convert-system-icons.cmd` from the project root.
3. If new: add a `Resource` field and a `MediaDbInitializer.AddMedia`
   call.
4. Commit both the `.svg` and the regenerated `.png`.

The script is idempotent — running it twice in a row produces no diff
unless an SVG actually changed.

Pros:

- **Server resources are the source of truth.** SVG remains the editable
  artwork; PNG is a derived artifact that lives next to it in the same
  folder, so designers can edit one file and re-run one script.
- **No new server endpoint, no new tool project, no Maui bundle bump,
  no runtime SVG conversion.** The smallest possible diff that fixes
  the actual breakage.
- **The only conversion code path is the one developers already
  understand from `prepare-sounds.cmd`** — a polyglot wrapper that
  shells out to a well-known image utility.
- **Re-runnable.** Designers can update an SVG, re-run the cmd, commit
  both files. No code change, no migration.

Cons:

- Repo holds both `.svg` and `.png` for each icon (~30 KB SVG + ~80 KB
  PNG total — six files each).
- A developer who edits an SVG must remember to re-run the script. A
  unit test catches this (see step 7 below).
- Developers need `rsvg-convert` installed once on their machine. See
  "Why `rsvg-convert`" below for install commands.

## Why `rsvg-convert`

I tried ImageMagick `convert` (which is what most people reach for
first) on `notes.svg` and the output is unusable: `notes.svg` is a
purple-blue gradient circle with a near-white pencil on top, and
ImageMagick rendered just the near-white pencil on a transparent
background — the entire gradient circle is missing. ImageMagick's
built-in MSVG renderer doesn't handle `<linearGradient>` referenced via
`fill="url(#…)"`, which **every one** of the six icons uses. The only
way to make `convert` work is to install an external rsvg or Inkscape
delegate, which is essentially "install rsvg or Inkscape anyway." So
ImageMagick is out.

The realistic cross-platform options:

| Tool | Install per platform | Output quality | Invocation |
|---|---|---|---|
| **`rsvg-convert`** (librsvg) | `apt install librsvg2-bin` / `brew install librsvg` / `scoop install librsvg` | excellent (Cairo backend, handles every SVG feature these icons use) | `rsvg-convert -w 512 -h 512 -o out.png in.svg` |
| **Inkscape** | `apt install inkscape` / `brew install --cask inkscape` / `choco install inkscape` | gold standard | `inkscape --export-type=png --export-filename=out.png --export-width=512 in.svg` |
| **Chrome headless** | every dev machine has Chrome | excellent | awkward (file:// or data: URL juggling) |

**Recommendation: `rsvg-convert`.** Single-purpose, tiny binary
(<2 MB on most platforms), Cairo-backed (same renderer GNOME/GTK ship
everywhere), trivial command line, handles every feature these icons
need, and the install command is one line on every platform. Inkscape
is a fine fallback for designers who already have it, but it's much
heavier and slower to invoke.

## Sizing the rasterised PNGs

- Server consumers don't care about size — they just store and serve
  the bytes.
- The Maui native consumers that today break on SVG
  (`IosIncomingShareSuggestions`, `AndroidIncomingShareSuggestions`,
  `ContactIconView`) display at logical sizes ≤ 160 px, i.e. up to
  ~480 px @3× retina.
- **Target 512 px**: comfortable retina headroom, modest file sizes
  (~80–150 KB per icon, ~600 KB total for the six). The cmd hard-codes
  this and we can change it in one place if designers want to tune.

For comparison, `MaxSize = 1920` (the migration flow / upload processor
default) would produce roughly 4–6× larger files for no display
benefit — 512 is the right number for this consumer set.

## The polyglot `convert-system-icons.cmd`

Lives at the project root next to `prepare-sounds.cmd` and `c.cmd`.
Bash + cmd polyglot mirroring `prepare-sounds.cmd`:

```cmd
:<<BATCH
    @echo off
    setlocal enabledelayedexpansion

    where rsvg-convert >nul 2>&1
    if errorlevel 1 (
        echo ERROR: rsvg-convert not found in PATH.
        echo Install with one of:
        echo   scoop install librsvg
        echo   choco install rsvg
        echo   winget install librsvg
        exit /b 1
    )

    set inDir=src\dotnet\Media.Service\Resources
    for %%F in (%inDir%\*.svg) do (
        echo Converting %%~nxF
        rsvg-convert -w 512 -h 512 -o %inDir%\%%~nF.png %%F || exit /b 1
    )
    exit /b 0
BATCH

#!/bin/sh
set -eu

if ! command -v rsvg-convert >/dev/null 2>&1; then
    cat >&2 <<EOF
ERROR: rsvg-convert not found in PATH.
Install with one of:
  apt install librsvg2-bin       # Debian / Ubuntu
  dnf install librsvg2-tools     # Fedora
  brew install librsvg           # macOS
EOF
    exit 1
fi

inDir="src/dotnet/Media.Service/Resources"
for svg in "$inDir"/*.svg; do
    name=$(basename "$svg" .svg)
    echo "Converting ${name}.svg"
    rsvg-convert -w 512 -h 512 -o "$inDir/${name}.png" "$svg"
done
```

Mark the file executable in git:
`git update-index --chmod=+x convert-system-icons.cmd`.

## Step-by-step tasks

1. **Add the polyglot `convert-system-icons.cmd`** at the project root
   with the contents above. Mark executable in git.

2. **Run it once** and commit the resulting six `.png` files into
   `src/dotnet/Media.Service/Resources/`.

3. **Wire the PNGs as embedded resources in `Media.Service.csproj`.**
   Verify the existing `EmbeddedResource` glob covers `*.png` under
   `Resources/`. If it only covers `*.svg`, broaden it to include both
   (`Resources/*.svg;Resources/*.png` or `Resources/*.*`).

4. **Update `Media.Service/Resources/Resource.cs`.** Switch each field
   to point at the PNG file. Either rename without the `Svg` suffix
   (preferred — and update the call sites in `MediaDbInitializer`) or
   keep the names and just change the file path. Recommend renaming
   to avoid `*Svg` fields that secretly point at PNGs.

5. **Update `Media.Service/Module/MediaDbInitializer.cs`** to pass the
   PNG `Resource` instances. `MediaUploader.AddMedia` already infers
   content type from the file extension, so the seed will land as
   `image/png` automatically.

6. **Verify `MediaUploader.AddMedia` idempotency on re-seed.** Re-running
   `MediaDbInitializer` on a DB that already has `system-icons:notes`
   pointing at a PNG blob must be a no-op. If it isn't (i.e. it
   clobbers the existing row on every restart), add an existence
   check before `AddMedia`. **This is the only behavioural risk in
   this plan.**

7. **Add a regression test** that boots `MediaDbInitializer` on an
   empty in-memory DB and asserts every `system-icons:*` media row
   has `ContentType == "image/png"` and a `BlobId` ending in `.png`.

8. **Add a one-line `README.md`** in
   `src/dotnet/Media.Service/Resources/` reading roughly:
   > SVGs in this folder are the source of truth. The matching PNGs
   > are derived artifacts produced by `./convert-system-icons.cmd`
   > in the project root (requires `rsvg-convert`). To add or edit a
   > system icon: drop the SVG, run the script, commit both files,
   > and add the `Resource` + `MediaDbInitializer` wiring if it's a
   > new icon.

### What we deliberately do NOT do

- We do **not** delete the `.svg` files. They stay as the editable
  source-of-truth artwork, which is the entire point of being able
  to re-run the script when a designer ships a tweak.
- We do **not** add SVG conversion to the runtime startup path or
  build path or any new server endpoint.
- We do **not** bundle anything into the Maui app. The Maui app
  continues to ship zero icon assets and continues to fetch through
  `IconUI` exactly as before — the only difference is that the URL
  it fetches now serves PNG bytes from a `.png`-extensioned blob
  (because the seed is PNG), so `imageproxy` and the iOS / Android
  native APIs are happy.
- We do **not** try to delete already-seeded `.svg` blobs from object
  storage. The migration flow has already rewritten the `DbMedia`
  rows' `BlobId` to PNG for any `system-icons:*` MediaId actually
  referenced from `DbChat.MediaId` / `DbAvatar.MediaId`. The orphaned
  `.svg` blobs in object storage are unreachable garbage and can be
  cleaned up by a separate sweep later.
- We do **not** flip `AvatarQuery.Format`'s default or touch the
  Marble/Beam SVG generators — out of scope.

## Acceptance criteria

- `convert-system-icons.cmd` runs cleanly on Windows, macOS, and
  Linux given a developer who has `rsvg-convert` installed; produces
  six PNGs in `src/dotnet/Media.Service/Resources/`; and is
  idempotent (running it twice in a row produces no diff).
- A fresh DB initialised by `MediaDbInitializer` has every
  `system-icons:*` `DbMedia` row with `ContentType = image/png` and
  `BlobId` ending in `.png` (covered by the regression test in step 7).
- Re-running `MediaDbInitializer` against a DB that already has the
  PNG-blob version of these rows is a no-op (step 6).
- Boot the server, create a Notes chat, open it in the iOS Hybrid app
  and trigger the iOS Share Extension: chat icon renders correctly.
  Today this is silently broken or shows a placeholder.
- Boot the server on Android, send a message to a system-icon chat,
  check the share suggestions surface — the chat appears with its
  icon.
- `dotnet build ActualChat.CI.slnf` is clean.
- Existing `IconSvgToPngMigrationFlowTest`, `IconUploadProcessorTest`,
  `ImageUploadProcessorTest` still pass — those cover the user-upload
  side and aren't touched by this change.

## Risks and notes

- **`MediaUploader.AddMedia` idempotency.** Step 6 above is the only
  behavioural risk. Verify before merging.
- **Designer review.** The PNGs are rasterised at 512 px with default
  `rsvg-convert` settings. Designers should sign off that they look
  acceptable at the share-extension display sizes. If not, the
  target size is a one-line edit in the cmd.
- **`rsvg-convert` install.** Developers need to install it once.
  The script prints a clear error and per-platform install commands
  if it isn't on PATH. Worth mentioning in `docs/AGENTS.md` or the
  team's onboarding doc as a "you'll want this if you ever touch
  system icons" note.
- **Sherlock.** The `system-icons:sherlock` row is referenced by the
  synthetic Sherlock author (`Constants.Sherlock.MediaId`) and was
  not covered by `IconSvgToPngMigrationFlow`'s avatar phase (no
  `DbAvatar` row), so on prod its `DbMedia` row may still point at
  the original `.svg` blob today. After steps 4–5 the next reseed
  flips it to PNG. Call this out in the PR description so ops know
  to expect the Sherlock blob to flip on the next deploy.
