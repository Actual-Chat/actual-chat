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

Added `HasPreview` property to `Attachment` class that returns true if the attachment has dimensions (indicating a preview was generated), regardless of whether the format is "supported".

### Files Modified

1. **`src/dotnet/UI.Blazor.App/Components/Attachment/Attachment.cs`**
   - Added `HasPreview` property: `IsImage || IsVideo || (Width > 0 && Height > 0)`
   - Updated `GetMetadataForUploadSession` to use `HasPreview`

2. **`src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/AttachmentItem.razor`**
   - Added fallback to render thumbnail as image when `HasPreview` is true

3. **`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Attachment/UploadAttachmentItem.razor`**
   - Added fallback to render thumbnail as image when `HasPreview` is true

4. **`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Attachment/ChatEntryAttachmentUploadsView.razor`**
   - Updated `GetOrderedAttachmentList` to use `HasPreview` for splitting visual/file lists

### Why This Is iOS-Specific

This change is effectively iOS-specific because:
- Only `IosFileProviderImpl.GetPreviewUrl()` generates JPG thumbnails for MOV files
- Only iOS sets Width/Height dimensions for MOV attachments during thumbnail generation
- Other platforms don't populate these fields for MOV files, so they continue showing file icons
