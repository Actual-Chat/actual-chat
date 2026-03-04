# iOS MOV Preview Thumbnail During Upload

## Problem

When uploading MOV files on iOS, the preview thumbnail is not displayed even though `IosFileProviderImpl.GetPreviewUrl()` generates a JPG thumbnail. The attachment falls back to showing a generic file icon.

## Root Cause

In `AttachmentItem.razor` and `UploadAttachmentItem.razor`, the preview rendering logic checks:
- `attachment.IsImage` → renders `<img>`
- `attachment.IsVideo` → renders `<video>`
- else → renders `FileAttachmentView` (file icon)

`IsVideo` uses `MediaTypeExt.IsSupportedVideo()` which only includes `video/mp4`. MOV files (`video/quicktime`) are not in this list, so they fall through to the file icon.

## Solution

Add an additional condition to render attachments that have a thumbnail preview (Width/Height set) but aren't recognized as supported image/video formats.

### Files to Modify

1. **`src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/AttachmentItem.razor`** (line ~31-39)
2. **`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Attachment/UploadAttachmentItem.razor`** (line ~31-37)

### Change

From:
```razor
@if (attachment.IsImage) {
    <img src="@previewUrl" alt="Image attachment" class="@plugClass"/>
} else if (attachment.IsVideo) {
    <video preload="metadata" class="@plugClass">
        <source src="@previewUrl#t=0.5"/>
    </video>
} else {
    <FileAttachmentView FileType="@attachment.FileType"/>
}
```

To:
```razor
@if (attachment.IsImage) {
    <img src="@previewUrl" alt="Image attachment" class="@plugClass"/>
} else if (attachment.IsVideo) {
    <video preload="metadata" class="@plugClass">
        <source src="@previewUrl#t=0.5"/>
    </video>
} else if (attachment.Width > 0 && attachment.Height > 0 && !previewUrl.IsNullOrEmpty()) {
    <img src="@previewUrl" alt="Video thumbnail" class="@plugClass"/>
} else {
    <FileAttachmentView FileType="@attachment.FileType"/>
}
```

### Why This Is iOS-Specific

This change is effectively iOS-specific because:
- Only `IosFileProviderImpl.GetPreviewUrl()` generates JPG thumbnails for MOV files
- Only iOS sets Width/Height dimensions for MOV attachments during the thumbnail generation process
- Other platforms don't populate these fields for MOV files, so they continue showing file icons

## Also Consider (ChatEntryAttachmentUploadsView.razor)

The split between visual and file attachments in `ChatEntryAttachmentUploadsView.razor:41-42`:
```csharp
=> (attachments.Where(a => a.IsImage || a.IsVideo).ToArray(),
    attachments.Where(a => a is { IsImage: false, IsVideo: false }).ToArray());
```

Should be updated to:
```csharp
=> (attachments.Where(a => a.IsImage || a.IsVideo || (a.Width > 0 && a.Height > 0)).ToArray(),
    attachments.Where(a => a is { IsImage: false, IsVideo: false } && a.Width == 0).ToArray());
```

This ensures MOV with thumbnails appears in the visual media section, not the file section.
