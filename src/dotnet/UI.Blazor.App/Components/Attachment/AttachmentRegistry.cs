using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentRegistry(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<AttachmentId, AttachmentInfo> _infos = new();
    private readonly ConcurrentDictionary<AttachmentId, AttachmentPreviewState> _previews = new();
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
            SetPreviewState(attachment.Id, AttachmentPreviewState.Preview(source.PreviewUrl));
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
            _ = GetPreviewState(id, default);
            _ = GetMediaContent(id, default);
            _ = GetFailureState(id, default);
        }
    }

    public void SetPreviewState(AttachmentId id, AttachmentPreviewState previewState)
    {
        _previews[id] = previewState;
        using (Invalidation.Begin())
            _ = GetPreviewState(id, default);
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
    public virtual async Task<bool> IsUploaded(AttachmentId id, CancellationToken cancellationToken)
    {
        var state = await GetAttachmentUploadState(id, cancellationToken).ConfigureAwait(false);
        return state.IsUploaded;
    }

    [ComputeMethod]
    public virtual async Task<AttachmentState> GetAttachmentState(AttachmentId id, CancellationToken cancellationToken)
    {
        var previewState = await GetPreviewState(id, cancellationToken).ConfigureAwait(false);
        var uploadState = await GetAttachmentUploadState(id, cancellationToken).ConfigureAwait(false);
        return new AttachmentState(previewState, uploadState);
    }

    [ComputeMethod]
    public virtual async Task<UploadState> GetAttachmentUploadState(AttachmentId id, CancellationToken cancellationToken)
    {
        var mediaContent = await GetMediaContent(id, cancellationToken).ConfigureAwait(false);
        if (mediaContent != null)
            return UploadState.Uploaded(mediaContent);
        var failureState = await GetFailureState(id, cancellationToken).ConfigureAwait(false);
        if (failureState == FailureState.Failed)
            return UploadState.Failed();
        var mediaStatus = await GetMediaStatus(id, cancellationToken).ConfigureAwait(false);
        // TODO(DF): add proper progress calculation
        var overallProgress = 0;
        if (mediaStatus is not null) {
            if (mediaStatus.Status > MediaStatus.Reserved) {
                var stageWidth = 30;
                if (mediaStatus.PreparingStage >= MediaPreparingStage.ServerProcessing) {
                    overallProgress = 70;
                    stageWidth = 30;
                }
                else if (mediaStatus.PreparingStage >= MediaPreparingStage.Uploading) {
                    overallProgress = 30;
                    stageWidth = 40;
                }
                overallProgress += (int)(mediaStatus.StageProgress * stageWidth / 100);
            }
        }
        if (failureState == FailureState.Restarting)
            return UploadState.InProgress(overallProgress);

        if (mediaStatus is null)
            return UploadState.Idle;

        if (mediaStatus.Status is MediaStatus.Ready)
            return UploadState.InProgress(99);

        if (mediaStatus.Status is MediaStatus.Failed)
            return UploadState.Failed();

        return UploadState.InProgress(overallProgress);
    }

    [ComputeMethod]
    public virtual Task<AttachmentPreviewState> GetPreviewState(AttachmentId id, CancellationToken cancellationToken)
    {
        var preview = _previews.GetValueOrDefault(id) ?? AttachmentPreviewState.PendingGetAccessRequest;
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
}

public sealed record AttachmentInfo(string UploadSessionId);

public enum PreviewAccessState {
    Ok, NoFileAccess, PendingGetAccessRequest
}

public sealed record AttachmentPreviewState(PreviewAccessState State, string PreviewUrl)
{
    public static readonly AttachmentPreviewState NoFileAccess = new(PreviewAccessState.NoFileAccess, "");
    public static readonly AttachmentPreviewState PendingGetAccessRequest = new(PreviewAccessState.PendingGetAccessRequest, "");
    public static readonly AttachmentPreviewState NoPreview = new(PreviewAccessState.Ok, "");
    public static AttachmentPreviewState Preview(string previewUrl) => new(PreviewAccessState.Ok, previewUrl);
}

public sealed record UploadState(MediaContent? UploadResult, int Progress, bool IsFailed)
{
    public static readonly UploadState Idle = new(null, 0, false);
    public static UploadState Uploaded(MediaContent mediaContent) => new (mediaContent, 100, false);
    public static UploadState Failed(int? progress = 0) => new (null, progress ?? 0, true);
    public static UploadState InProgress(int progress) => new (null, progress, false);

    public bool IsUploaded => UploadResult != null;
}

public sealed record AttachmentState(AttachmentPreviewState Preview, UploadState UploadState)
{
    public static readonly AttachmentState None = new(AttachmentPreviewState.NoPreview, UploadState.Idle);

    public bool NoAccess => Preview.State == PreviewAccessState.NoFileAccess;

    public string UploadSessionId { get; init; } = "";
}

public enum FailureState
{
    None,
    Failed,
    Restarting,
}
