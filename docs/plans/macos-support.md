# macOS (Mac Catalyst): full platform support

## Goal
Bring the Mac Catalyst app to parity with the other platforms, so macOS is a
first-class target rather than iOS code that happens to compile. Today most of
the Apple-specific code is shared with iOS via `MaciOS/`, which is right for the
parts where the platforms agree — and wrong wherever macOS has its own
conventions or its own capabilities.

Nothing here is scheduled. This is a place to record the gaps as they're found,
so they aren't rediscovered one at a time.

## Why this exists as a doc
Mac Catalyst reuses iOS implementations by default: `MauiProgram.MacCatalyst.cs`
registers the same services as `MauiProgram.iOS.cs`. That default is invisible —
a service written for iOS silently becomes the macOS behavior too, with no
compile error and often no runtime error, just wrong-feeling UX. Each such case
needs a deliberate decision: share, or split.

Two narrower macOS plans already exist and stay where they are:
[MacCatalyst voice processing](./maccatalyst-voice-processing.md) and
[macOS notification permission](./macos-notification-permission.md).

## Known gaps

### Saving downloaded files uses the iOS strategy

**Status:** not started. Found 2026-08-01 while implementing native downloads.

`IFileSaver` is implemented for Apple platforms by
`App.Maui/MaciOS/AppleFileSaver.cs`, and `MauiProgram.MacCatalyst.cs` registers
it for macOS too. It branches on `MediaTypeExt.IsSupportedVisualMedia`:

- images and video → `PHPhotoLibrary` (silent save into Photos)
- everything else → download to a temp file, then the share sheet

Both halves are chosen for iOS constraints that **do not apply to macOS**:

1. **Photos is the wrong destination on a Mac.** On iOS, Photos is the only
   user-visible location an app may write to silently, so media goes there. On
   macOS, "Download this picture" conventionally means `~/Downloads`. Importing
   into Photos is surprising, and it costs a photo-library permission prompt
   that macOS users won't expect for a download.
2. **The share sheet may have nowhere to save.** On iOS the sheet's value is the
   **"Save to Files"** item. macOS's share menu is AirDrop / Mail / Messages /
   Notes / Reminders — there is no "Save to Files" equivalent, because macOS
   apps can write to disk directly. So a Mac user may get a share sheet that
   offers no way to actually save the file.

**Suggested fix:** split the Apple saver — keep `AppleFileSaver` for iOS, and
add a Mac Catalyst implementation that either writes straight to `~/Downloads`
and shows a toast (mirroring `AndroidFileSaver`'s silent-save behavior), or
presents an `NSSavePanel`-backed picker. Either is native and silent-or-expected,
unlike a share sheet.

**Caveat:** the analysis above is from the platform APIs, **not observed** — the
Mac Catalyst target was never built or run when this was written (only the iOS
TFM was). Verify the actual share-sheet contents on a Mac before designing the
fix; the Mac Mini can build and run Catalyst.

For how the other platforms behave, see the table in
[Downloads and file saving](#downloads-and-file-saving) below.

## Reference: downloads and file saving today

Every "Download" / "Save" action goes through `FileDownloadUI`
(`UI.Blazor/Services/FileDownload/`), which dispatches to `IFileSaver` where a
platform registers one, and otherwise falls back to a browser blob download.

| | Images / video | Audio | Other files |
|---|---|---|---|
| iOS | Photos, silent | share sheet | share sheet |
| **macOS** | **Photos, silent** | **share sheet** | **share sheet** |
| Android | gallery, silent | `Music/`, silent | `Downloads/`, silent |
| Windows | browser download | browser download | browser download |
| Web | browser download | browser download | browser download |

macOS shares the iOS row because it shares the implementation. That's the gap.
