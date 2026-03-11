using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public enum PreviewAccessState { Ok, NoFileAccess, PendingGetAccessRequest }

public sealed record AttachmentPreview(PreviewAccessState State, FilePreview? Preview)
{
    public static readonly AttachmentPreview NoFileAccess = new(PreviewAccessState.NoFileAccess, null);
    public static readonly AttachmentPreview PendingGetAccessRequest = new(PreviewAccessState.PendingGetAccessRequest, null);
    public static readonly AttachmentPreview NoPreview = new(PreviewAccessState.Ok, null);

    public string PreviewUrl => Preview?.Url ?? "";

    public static AttachmentPreview From(FilePreview? preview)
        => preview is null ? NoPreview : new(PreviewAccessState.Ok, preview);
}
