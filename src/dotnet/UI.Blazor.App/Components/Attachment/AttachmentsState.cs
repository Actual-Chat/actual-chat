using System.Collections.Frozen;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentsState(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly FrozenDictionary<MediaStage, StageProgressInfo> StageProgressMap = BuildStageProgressMap();
    private static readonly MediaId FakeMediaId = MediaId.New("client-id:upload-ready");
    private readonly ConcurrentDictionary<AttachmentId, AttachmentInfo> _infos = new();
    private readonly ConcurrentDictionary<AttachmentId, AttachmentPreview> _previews = new();
    private readonly ConcurrentDictionary<AttachmentId, MediaContent> _mediaContents = new();
    private readonly ConcurrentDictionary<AttachmentId, FailureState> _failureStates = new();
    private readonly ConcurrentDictionary<string, PropertyBag> _uploadSessionMetadata = new(StringComparer.Ordinal);

    public void Register(Attachment attachment)
    {
        if (attachment.UploadSessionId.IsNullOrEmpty())
            throw new InvalidOperationException("Attachment upload is not initialized yet.");
        if (!_infos.TryAdd(attachment.Id, new AttachmentInfo(attachment.UploadSessionId)))
            throw new InvalidOperationException("Attachment already registered");
        using (Invalidation.Begin())
            _ = GetAttachmentInfo(attachment.Id, default);
        if (attachment is SourceAttachment source)
            SetPreview(attachment.Id, AttachmentPreview.Preview(source.PreviewUrl));
        SetUploadSessionMetadata(attachment.UploadSessionId, attachment);
    }

    public void Unregister(AttachmentId id)
    {
        _infos.TryRemove(id, out _);
        _previews.TryRemove(id, out _);
        _mediaContents.TryRemove(id, out _);
        _failureStates.TryRemove(id, out _);
        using (Invalidation.Begin()) {
            _ = GetAttachmentInfo(id, default);
            _ = GetPreview(id, default);
            _ = GetMediaContent(id, default);
            _ = GetFailureState(id, default);
        }
    }

    public void SetPreview(AttachmentId id, AttachmentPreview preview)
    {
        _previews[id] = preview;
        using (Invalidation.Begin())
            _ = GetPreview(id, default);
    }

    public void SetMediaContent(AttachmentId id, MediaContent mediaContent)
    {
        _mediaContents[id] = mediaContent;
        using (Invalidation.Begin())
            _ = GetMediaContent(id, default);
    }

    public void SetFailureState(AttachmentId id, FailureState failureState)
    {
        if (failureState == FailureState.None)
            _failureStates.TryRemove(id, out _);
        else
            _failureStates[id] = failureState;
        using (Invalidation.Begin())
            _ = GetFailureState(id, default);
    }

    public void SetUploadSessionMetadata(string uploadSessionId, Attachment attachment)
    {
        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), attachment.FileName)
            .Set(nameof(Media.Media.ContentType), attachment.FileType)
            .Set(nameof(Media.Media.Length), attachment.Length);
        if (attachment.IsImage || attachment.IsVideo)
            metadata = metadata
                .Set(nameof(Media.Media.Width), attachment.Width)
                .Set(nameof(Media.Media.Height), attachment.Height);
        SetUploadSessionMetadata(uploadSessionId, metadata);
    }

    public void SetUploadSessionMetadata(string uploadSessionId, PropertyBag metadata)
    {
        _uploadSessionMetadata[uploadSessionId] = metadata;
        using (Invalidation.Begin())
            _ = GetUploadSessionMetadata(uploadSessionId, default);
    }

    public void RemoveUploadSessionMetadata(string uploadSessionId)
    {
        _uploadSessionMetadata.TryRemove(uploadSessionId, out _);
        using (Invalidation.Begin())
            _ = GetUploadSessionMetadata(uploadSessionId, default);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsReady(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = await GetProgress(id, cancellationToken).ConfigureAwait(false);
        return state.IsReady;
    }

    [ComputeMethod]
    public virtual async Task<AttachmentState> GetAttachmentState(AttachmentId id, CancellationToken cancellationToken)
    {
        var previewState = await GetPreview(id, cancellationToken).ConfigureAwait(false);
        var uploadState = await GetProgress(id, cancellationToken).ConfigureAwait(false);
        return new AttachmentState(previewState, uploadState);
    }

    public virtual async Task<AttachmentProgress> GetProgress(AttachmentId id, CancellationToken cancellationToken)
    {
        var sessionId = await GetUploadSessionId(id, cancellationToken);
        if (sessionId.IsNullOrEmpty())
            return AttachmentProgress.New;

        var uploadProgress = await Hub.UploadSessionsState.GetProgress(sessionId, cancellationToken).ConfigureAwait(false);
        MediaStatusInfo mediaStatus;
        if (uploadProgress.Stage == UploadStage.Completed)
            mediaStatus = new(FakeMediaId, MediaStage.Ready, 0, "");
        else if (uploadProgress.Stage >= UploadStage.Uploaded) {
            var mediaStatus1 = await GetMediaStatus(sessionId, cancellationToken).ConfigureAwait(false);
            mediaStatus = mediaStatus1 ?? new(FakeMediaId, MediaStage.Uploaded, 0, "");
        }
        else {
            MediaStage stage = uploadProgress.Stage switch {
                UploadStage.Uploading => MediaStage.Uploading,
                UploadStage.ClientProcessing => MediaStage.ClientProcessing,
                UploadStage.New => MediaStage.Reserved,
                _ => throw new InvalidOperationException($"Unexpected upload stage: {uploadProgress.Stage}"),
            };
            mediaStatus = new MediaStatusInfo(FakeMediaId, stage, uploadProgress.Progress, uploadProgress.ErrorMessage);
        }

        var stageInfo = GetStateInfo(mediaStatus.Stage);
        var overallProgress = stageInfo.BaseProgress
            + (int)(mediaStatus.StageProgress * stageInfo.StageWidth / 100);

        var isReady = mediaStatus.Stage == MediaStage.Ready;
        var isFailed = !isReady && mediaStatus.HasFailed;
        var details = (isReady, isFailed) switch {
            (true, _) => "",
            (_, true) => "Failed: " + mediaStatus.ErrorMessage,
            _ => stageInfo.Details,
        };
        return new AttachmentProgress(overallProgress, details) {
            IsReady = isReady,
            IsFailed = mediaStatus.HasFailed,
        };
    }

    [ComputeMethod]
    public virtual Task<AttachmentPreview> GetPreview(AttachmentId id, CancellationToken cancellationToken)
    {
        var preview = _previews.GetValueOrDefault(id) ?? AttachmentPreview.PendingGetAccessRequest;
        return Task.FromResult(preview);
    }

    [ComputeMethod]
    public virtual Task<MediaContent?> GetMediaContent(AttachmentId id, CancellationToken cancellationToken)
    {
        var mediaContent = _mediaContents.GetValueOrDefault(id);
        return Task.FromResult(mediaContent);
    }

    [ComputeMethod]
    public virtual Task<FailureState> GetFailureState(AttachmentId id, CancellationToken cancellationToken)
    {
        var failureState = _failureStates.GetValueOrDefault(id);
        return Task.FromResult(failureState);
    }

    [ComputeMethod]
    public virtual Task<PropertyBag> GetUploadSessionMetadata(string uploadSessionId, CancellationToken cancellationToken)
    {
        var metadata = _uploadSessionMetadata.GetValueOrDefault(uploadSessionId);
        return Task.FromResult(metadata);
    }

    [ComputeMethod]
    public virtual Task<AttachmentInfo?> GetAttachmentInfo(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = _infos.GetValueOrDefault(id);
        return Task.FromResult(state);
    }

    private async Task<MediaStatusInfo?> GetMediaStatus(string sessionId, CancellationToken cancellationToken)
    {
        var mediaId = await Hub.UploadSessionsState.GetReservedMediaId(sessionId, cancellationToken);
        if (mediaId is null)
            return null;

        return await Hub.Medias.GetStatus(Session, mediaId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetUploadSessionId(AttachmentId id, CancellationToken cancellationToken)
    {
        var info = await GetAttachmentInfo(id, cancellationToken).ConfigureAwait(false);
        return info?.UploadSessionId;
    }

    private StageProgressInfo GetStateInfo(MediaStage stage)
    {
        var info = StageProgressMap.GetValueOrDefault(stage);
        return info ?? new StageProgressInfo(30, 0, "Unknown stage");
    }

    private static FrozenDictionary<MediaStage, StageProgressInfo> BuildStageProgressMap()
    {
        // Only BaseProgress and Details are specified — StageWidth is computed automatically
        var stages = new (MediaStage Stage, int BaseProgress, string Details)[] {
            (MediaStage.Reserved, 0, ""),
            (MediaStage.ClientProcessing, 2, "Client processing"),
            (MediaStage.Uploading, 25, "Uploading"),
            (MediaStage.Uploaded, 70, "Uploaded"),
            (MediaStage.ServerProcessing, 70, "Server processing"),
            (MediaStage.Saving, 97, "Saving"),
            (MediaStage.Ready, 100, "Ready"),
        };

        var result = new Dictionary<MediaStage, StageProgressInfo>();
        for (var i = 0; i < stages.Length; i++) {
            var (stage, baseProgress, details) = stages[i];
            var nextBaseProgress = i + 1 < stages.Length ? stages[i + 1].BaseProgress : 100;
            var stageWidth = nextBaseProgress - baseProgress;
            result[stage] = new StageProgressInfo(baseProgress, stageWidth, details);
        }
        return result.ToFrozenDictionary();
    }

    // Nested types
    private sealed record StageProgressInfo(int BaseProgress, int StageWidth, string Details);
}

public sealed record AttachmentInfo(string UploadSessionId);

public sealed record AttachmentState(AttachmentPreview Preview, AttachmentProgress Progress)
{
    public static readonly AttachmentState None = new(AttachmentPreview.NoPreview, AttachmentProgress.New);

    public bool NoAccess => Preview.State == PreviewAccessState.NoFileAccess;

    public string UploadSessionId { get; init; } = "";
}

public enum FailureState
{
    None,
    Failed,
    Restarting,
}
