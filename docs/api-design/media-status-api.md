# Media Status API - Proposal

## Overview

API for tracking media content status in ActualChat with detailed processing progress.

## Quick Start

### What is this?

API for tracking media content status (chat/place pics, avatars, text entry attachments, etc.) with detailed processing progress.

### Main Idea

Media preparation process starts with reserving MediaId.
After that, MediaId can be used to upload media content and track upload progress.
The same upload workflow can be used for any media content: chat/place pics, avatars, text entry attachments, etc.

```
User selects file
  ↓ Reserved (MediaId is known immediately)
  ↓ MediaId can be assigned to any consumer (text entry attachment, chat picture, avatar, etc.) right away or after upload completes.
  ↓ Preparing: Transcoding (45%)
  ↓ Preparing: Uploading (60%)
  ↓ Preparing: GeneratingThumbnail
  ↓ Preparing: Converting
  ↓ Ready ✓ (100%)
  ↓ MediaId can be assigned to any consumer (text entry attachment, chat picture, avatar, etc.) if not yet.
```

### Key Features

1. **Status and stage separation**
   - `Status`: Reserved → Preparing → Ready
   - `Stage`: Transcoding → Compressing → Uploading → etc.

2. **Detailed progress**
   - `StageProgress`: current stage progress (0.0-1.0)
   - `OverallProgress`: overall progress across all stages

3. **Reactivity**
   - Fusion Compute Methods automatically update UI
   - All users see progress in real-time, but with different levels of detail depending on the client.

## Key Types

### MediaStatus
```csharp
enum MediaStatus {
    Reserved,    // Reserved
    Preparing,   // In progress (see PreparingStage)
    Ready,       // Ready for use
    Failed       // Error
}
```

// Here are the stages that can be in Preparing state.
### MediaPreparingStage
```csharp
// There are 3 main stages: client-side processing, uploading to server, processing on server.
// Each stage can have multiple sub-stages.
enum MediaPreparingStage {
    None = 0x00,
    
    ClientProcessing = 0x40,      // Client-side processing
    Transcoding      = 0x41,      // Video/audio transcoding
    Compressing      = 0x42,      // Image compression
    
    Uploading        = 0x80,      // Uploading to server
    
    ServerProcessing    = 0xC0,   // Server-side processing
    Converting          = 0xC1,   // Format conversion
    GeneratingThumbnail = 0xC2,   // Preview generation
    ProcessingMetadata  = 0xC3,   // Metadata processing
    Validating          = 0xC4,   // Validation
    VirusScanning       = 0xC5,   // Antivirus
}
```

### MediaStatusInfo
```csharp
record MediaStatusInfo {
    MediaId Id;
    MediaStatus Status;
    MediaPreparingStage PreparingStage;
    double StageProgress;        // 0.0-1.0 for current stage
    double OverallProgress { get; }  // Overall progress
    string ThumbnailContentId;   // Temporary thumbnail
}
NOTE: Should we store link to thumbnail content id here or on the Media?
```

## Typical Workflow

### Upload with detailed stages

```csharp
// 1. Reserve MediaId
var mediaId = await mediaContent.ReserveMediaId(session, scope, metadata, ct);
// → Status: Reserved, PreparingStage: None

// 2. Start processing
await uploadProgress.MarkPreparing(
    session, mediaId,
    MediaPreparingStage.Transcoding);
// → Status: Preparing, PreparingStage: Transcoding

// 3. Link with upload and update progress
await uploadProgress.LinkToUpload(session, mediaId, uploadId);
await uploadProgress.MarkPreparing(
    session, mediaId,
    MediaPreparingStage.Uploading);
// Since we're uploading, stage progress is captured from upload progress.

// 4. After upload completes, we start processing on server.
// This job can be started from the client or on the server from upload completed event.
await uploadProgress.MarkPreparing(
    session, mediaId,
    MediaPreparingStage.ServerProcessing);

// 5. Complete
After content processing is completed and we have a contentId, mark media as ready.
await uploadProgress.MarkReady(session, mediaId, contentId);
// → Status: Ready
```

### UI Display
Client initiated uploads can show detailed progress in the UI.
```razor
@if (status?.IsPreparing == true) {
    <div class="processing">
        <progress value="@status.OverallProgress" max="1.0" />
        <span>@(status.OverallProgress:P0)</span>

        <div>
            @status.StageDescription
            @if (status.StageProgress is { } progress) {
                <span>@(progress:P0)</span>
            }
        </div>
    </div>
}
else if (status?.IsReady == true) {
    <img src="@GetMediaUrl()" />
}
```
Other clients can show a simple progress bar.

## Architecture

## Roadmap

## Q&A

### Why separate status and stage?
Status answers "is it ready?", stage answers "what's being done?".
These are different concepts that should be independent.

```
Status: Preparing (not ready)
Stage: Uploading (uploading to server)
```

### Can I add my own stage?
Yes! Simply add to `MediaPreparingStage` enum.
No need to touch statuses (`MediaLifecycleStatus`).

### How does OverallProgress work?
```csharp
OverallProgress = offset_of_stage + (StageProgress * weight_of_stage)

Example (sequential processing):
  Transcoding: weight 30%, offset 0%    → 0-30%
  Uploading:   weight 30%, offset 50%   → 50-80%
  etc.

Example (parallel processing):
  OverallProgress = (transcodedChunks + uploadedChunks) / (totalChunks * 2)
  StageProgress = progress of priority operation (e.g., transcoding)
```

### How does parallel processing work?
For parallel operations (e.g., transcoding + uploading simultaneously):
- Show **priority stage** (slowest/most important)
- `StageProgress` = progress of that stage
- `OverallProgress` = overall progress of all operations

```csharp
// Select priority stage
var stage = isTranscoding ? Transcoding : Uploading;
var stageProgress = GetProgress(stage);

// OverallProgress accounts for both operations
var overallProgress = (transcodedChunks + uploadedChunks) / (totalChunks * 2);
```
