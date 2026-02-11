using System.Collections.Frozen;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentRegistry(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly FrozenDictionary<MediaStage, StageProgressInfo> StageProgressMap = BuildStageProgressMap();
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
        var state = await GetAttachmentUploadState(id, cancellationToken).ConfigureAwait(false);
        return state.IsReady;
    }

    [ComputeMethod]
    public virtual async Task<AttachmentState> GetAttachmentState(AttachmentId id, CancellationToken cancellationToken)
    {
        var previewState = await GetPreview(id, cancellationToken).ConfigureAwait(false);
        var uploadState = await GetAttachmentUploadState(id, cancellationToken).ConfigureAwait(false);
        return new AttachmentState(previewState, uploadState);
    }

    public virtual async Task<AttachmentUploadState> GetAttachmentUploadState(
        AttachmentId id,
        CancellationToken cancellationToken)
    {
        var mediaStatus = await GetMediaStatus(id, cancellationToken).ConfigureAwait(false);
        if (mediaStatus is null)
            return AttachmentUploadState.Idle;

        var stageInfo = GetStateInfo(mediaStatus.Stage);
        var overallProgress = stageInfo.BaseProgress
            + (int)(mediaStatus.StageProgress * stageInfo.StageWidth / 100);

        var details = mediaStatus.HasFailed
            ? "Failed: " + mediaStatus.ErrorMessage
            : stageInfo.Details;

        return new AttachmentUploadState(mediaStatus.Stage, overallProgress, details) {
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
    public virtual async Task<MediaStatusInfo?> GetMediaStatus(AttachmentId id, CancellationToken cancellationToken)
    {
        var info = await GetAttachmentInfo(id, cancellationToken).ConfigureAwait(false);
        var uploadSessionId = info?.UploadSessionId;
        if (uploadSessionId.IsNullOrEmpty())
            return null;

        var mediaId = await Hub.UploadSessionsState.GetReservedMediaId(uploadSessionId, cancellationToken);
        if (mediaId is null)
            return null;

        var status = await Hub.Medias.GetStatus(Session, mediaId, cancellationToken).ConfigureAwait(false);
        return status;
    }

    [ComputeMethod]
    public virtual Task<AttachmentInfo?> GetAttachmentInfo(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = _infos.GetValueOrDefault(id);
        return Task.FromResult(state);
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

public sealed record AttachmentUploadState(MediaStage Stage, double Progress, string Details = "")
{
    public static readonly AttachmentUploadState Idle = new(MediaStage.Reserved, 0);

    public bool IsReady => Stage == MediaStage.Ready;
    public bool IsFailed { get; init; }
}

public sealed record AttachmentState(AttachmentPreview Preview, AttachmentUploadState UploadState)
{
    public static readonly AttachmentState None = new(AttachmentPreview.NoPreview, AttachmentUploadState.Idle);

    public bool NoAccess => Preview.State == PreviewAccessState.NoFileAccess;

    public string UploadSessionId { get; init; } = "";
}

public enum FailureState
{
    None,
    Failed,
    Restarting,
}
