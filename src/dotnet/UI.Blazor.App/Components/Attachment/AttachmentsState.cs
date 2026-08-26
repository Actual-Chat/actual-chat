using ActualChat.Localization;
using System.Collections.Frozen;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentsState(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly FrozenDictionary<UploadStage, StageProgressInfo> StageProgressMap = BuildStageProgressMap();
    private readonly ConcurrentDictionary<AttachmentId, AttachmentInfo> _infos = new();
    private readonly ConcurrentDictionary<AttachmentId, AttachmentPreview> _previews = new();
    private UploadSessionsState UploadSessionsState => Hub.UploadSessionsState;

    public void Register(Attachment attachment)
    {
        var uploadSessionId = attachment.UploadSessionId;
        if (uploadSessionId.IsNullOrEmpty())
            throw new InvalidOperationException("Attachment upload is not initialized yet.");
        if (!_infos.TryAdd(attachment.Id, new AttachmentInfo(uploadSessionId)))
            throw new InvalidOperationException("Attachment already has been registered");
        using (Invalidation.Begin())
            _ = GetAttachmentInfo(attachment.Id, default);

        if (attachment is SourceAttachment source)
            SetPreview(attachment.Id, AttachmentPreview.From(source.Preview));
    }

    public void Unregister(AttachmentId id)
    {
        _infos.TryRemove(id, out _);
        _previews.TryRemove(id, out _);
        using (Invalidation.Begin()) {
            _ = GetAttachmentInfo(id, default);
            _ = GetPreview(id, default);
        }
    }

    public void SetPreview(AttachmentId id, AttachmentPreview preview)
    {
        _previews[id] = preview;
        using (Invalidation.Begin())
            _ = GetPreview(id, default);
    }

    [ComputeMethod]
    public virtual async Task<AttachmentProgress> GetProgress(AttachmentId id, CancellationToken cancellationToken)
    {
        var sessionId = await GetUploadSessionId(id, cancellationToken);
        if (sessionId.IsNullOrEmpty())
            return AttachmentProgress.New;

        var uploadProgress = await UploadSessionsState.GetProgress(sessionId, cancellationToken).ConfigureAwait(false);
        var stageInfo = GetStateInfo(uploadProgress.Stage);
        var stageDetails = GetStageDetails(uploadProgress.Stage);
        var overallProgress = stageInfo.BaseProgress
            + (int)(uploadProgress.StageProgress * stageInfo.StageWidth / 100);

        var totalBytes = uploadProgress.TotalBytes;
        var uploadedBytes = (long)(totalBytes * uploadProgress.UploadedFraction);

        var isReady = uploadProgress.IsReady;
        var isFailed = uploadProgress.IsFailed;
        var details = (isReady, isFailed) switch {
            (true, _) => "",
            (_, true) => L.Attachment_Failed_Format(uploadProgress.ErrorMessage.NullIfEmpty() ?? stageDetails),
            _ => stageDetails,
        };
        return new AttachmentProgress(overallProgress, details) {
            IsInProgress = !isReady && !isFailed,
            IsReady = isReady,
            IsFailed = isFailed,
            UploadProgress = uploadedBytes,
            UploadSize = totalBytes,
        };
    }

    [ComputeMethod]
    public virtual async Task<bool> IsReady(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = await GetProgress(id, cancellationToken).ConfigureAwait(false);
        return state.IsReady;
    }

    [ComputeMethod]
    public virtual Task<AttachmentPreview> GetPreview(AttachmentId id, CancellationToken cancellationToken)
    {
        var preview = _previews.GetValueOrDefault(id) ?? AttachmentPreview.PendingGetAccessRequest;
        return Task.FromResult(preview);
    }

    [ComputeMethod]
    protected virtual Task<AttachmentInfo?> GetAttachmentInfo(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = _infos.GetValueOrDefault(id);
        return Task.FromResult(state);
    }

    private async Task<string?> GetUploadSessionId(AttachmentId id, CancellationToken cancellationToken)
    {
        var info = await GetAttachmentInfo(id, cancellationToken).ConfigureAwait(false);
        return info?.UploadSessionId;
    }

    private static StageProgressInfo GetStateInfo(UploadStage stage)
    {
        var info = StageProgressMap.GetValueOrDefault(stage);
        return info ?? new StageProgressInfo(30, 0);
    }

    private string GetStageDetails(UploadStage stage)
        => stage switch {
            UploadStage.New => "",
            UploadStage.ClientProcessing => L.Attachment_ClientProcessing,
            UploadStage.Uploading => L.Attachment_Uploading,
            UploadStage.Uploaded => L.Attachment_Uploaded,
            UploadStage.ServerProcessing => L.Attachment_ServerProcessing,
            UploadStage.Completed => L.Attachment_Ready,
            _ => L.Attachment_UnknownStage,
        };

    private static FrozenDictionary<UploadStage, StageProgressInfo> BuildStageProgressMap()
    {
        // Only BaseProgress is specified — StageWidth is computed automatically
        var stages = new (UploadStage Stage, int BaseProgress)[] {
            (UploadStage.New, 0),
            (UploadStage.ClientProcessing, 2),
            (UploadStage.Uploading, 25),
            (UploadStage.Uploaded, 70),
            (UploadStage.ServerProcessing, 70),
            (UploadStage.Completed, 100),
        };

        var result = new Dictionary<UploadStage, StageProgressInfo>();
        for (var i = 0; i < stages.Length; i++) {
            var (stage, baseProgress) = stages[i];
            var nextBaseProgress = i + 1 < stages.Length ? stages[i + 1].BaseProgress : 100;
            var stageWidth = nextBaseProgress - baseProgress;
            result[stage] = new StageProgressInfo(baseProgress, stageWidth);
        }
        return result.ToFrozenDictionary();
    }

    // Nested types
    protected sealed record AttachmentInfo(string UploadSessionId);

    private sealed record StageProgressInfo(int BaseProgress, int StageWidth);
}
