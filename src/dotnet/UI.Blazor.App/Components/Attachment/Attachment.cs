using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Components;

public record Attachment(string PreviewUrl, string FileName, string FileType, IAttachRequest Request)
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public int Progress { get; init; }
    public MediaId? MediaId { get; init; }
    public MediaId? ThumbnailMediaId { get; init; }
    [MemberNotNullWhen(true, nameof(MediaId))]
    public bool Uploaded => MediaId != null;
    public bool Failed { get; init; }
    public bool NoAccess { get; init; }

    public string UploadSessionId => Request is UploadSessionAttachRequest request ? request.UploadSessionId : "";

    public bool IsImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsVideo => MediaTypeExt.IsSupportedVideo(FileType);

    public event Func<AttachmentList, Attachment, Task>? RemovedFromList;
    public event Func<AttachmentList, Attachment, Task>? RestartUploadRequested;

    public Task RaiseRemovedFromList(AttachmentList list)
        => RemovedFromList?.Invoke(list, this) ?? Task.CompletedTask;

    public Task RaiseRestartUploadRequested(AttachmentList list)
        => RestartUploadRequested?.Invoke(list, this) ?? Task.CompletedTask;
}
