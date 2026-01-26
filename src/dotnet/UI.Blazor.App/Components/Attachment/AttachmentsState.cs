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

    [ComputeMethod]
    public virtual async Task<bool> IsReady(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = await GetProgress(id, cancellationToken).ConfigureAwait(false);
        return state.IsReady;
    }

    public virtual async Task<AttachmentProgress> GetProgress(AttachmentId id, CancellationToken cancellationToken)
    {
        var sessionId = await GetUploadSessionId(id, cancellationToken);
        if (sessionId.IsNullOrEmpty())
            return AttachmentProgress.New;

        var uploadProgress = await Hub.UploadSessionsState.GetProgress(sessionId, cancellationToken).ConfigureAwait(false);
        MediaStatusInfo mediaStatus;
        if (uploadProgress.Stage == UploadStage.Completed)
            mediaStatus = new(FakeMediaId, 0, MediaStage.Ready, 0, "");
        else if (uploadProgress.Stage >= UploadStage.Uploaded) {
            var mediaStatus1 = await GetMediaStatus(sessionId, cancellationToken).ConfigureAwait(false);
            mediaStatus = mediaStatus1 ?? new(FakeMediaId, 0, MediaStage.Uploaded, 0, "");
        }
        else {
            MediaStage stage = uploadProgress.Stage switch {
                UploadStage.Uploading => MediaStage.Uploading,
                UploadStage.ClientProcessing => MediaStage.ClientProcessing,
                UploadStage.New => MediaStage.Reserved,
                _ => throw new InvalidOperationException($"Unexpected upload stage: {uploadProgress.Stage}"),
            };
            mediaStatus = new MediaStatusInfo(FakeMediaId, 0, stage, uploadProgress.Progress, uploadProgress.ErrorMessage);
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
            IsInProgress = !isReady && !isFailed,
            IsReady = isReady,
            IsFailed = isFailed,
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

public enum FailureState
{
    None,
    Failed,
    Restarting,
}
