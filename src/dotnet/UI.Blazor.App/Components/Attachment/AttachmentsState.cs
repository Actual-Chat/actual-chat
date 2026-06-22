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
        var overallProgress = stageInfo.BaseProgress
            + (int)(uploadProgress.StageProgress * stageInfo.StageWidth / 100);

        var uploadFraction = uploadProgress.Stage switch {
            UploadStage.New or UploadStage.ClientProcessing => 0d,
            UploadStage.Uploading => Math.Clamp(uploadProgress.StageProgress / 100d, 0d, 1d),
            _ => 1d, // Uploaded / ServerProcessing / Completed — bytes are already uploaded
        };
        var totalBytes = uploadProgress.TotalBytes;
        var uploadedBytes = (long)(totalBytes * uploadFraction);

        var isReady = uploadProgress.IsReady;
        var isFailed = uploadProgress.IsFailed;
        var details = (isReady, isFailed) switch {
            (true, _) => "",
            (_, true) => "Failed: " + (uploadProgress.ErrorMessage.NullIfEmpty() ?? stageInfo.Details),
            _ => stageInfo.Details,
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
        return info ?? new StageProgressInfo(30, 0, "Unknown stage");
    }

    private static FrozenDictionary<UploadStage, StageProgressInfo> BuildStageProgressMap()
    {
        // Only BaseProgress and Details are specified — StageWidth is computed automatically
        var stages = new (UploadStage Stage, int BaseProgress, string Details)[] {
            (UploadStage.New, 0, ""),
            (UploadStage.ClientProcessing, 2, "Client processing"),
            (UploadStage.Uploading, 25, "Uploading"),
            (UploadStage.Uploaded, 70, "Uploaded"),
            (UploadStage.ServerProcessing, 70, "Server processing"),
            (UploadStage.Completed, 100, "Ready"),
        };

        var result = new Dictionary<UploadStage, StageProgressInfo>();
        for (var i = 0; i < stages.Length; i++) {
            var (stage, baseProgress, details) = stages[i];
            var nextBaseProgress = i + 1 < stages.Length ? stages[i + 1].BaseProgress : 100;
            var stageWidth = nextBaseProgress - baseProgress;
            result[stage] = new StageProgressInfo(baseProgress, stageWidth, details);
        }
        return result.ToFrozenDictionary();
    }

    // Nested types
    protected sealed record AttachmentInfo(string UploadSessionId);

    private sealed record StageProgressInfo(int BaseProgress, int StageWidth, string Details);
}
