namespace ActualChat.UI.Blazor.App.Components;

public enum PreviewAccessState { Ok, NoFileAccess, PendingGetAccessRequest }

public sealed record AttachmentPreview(PreviewAccessState State, string PreviewUrl)
{
    public static readonly AttachmentPreview NoFileAccess = new(PreviewAccessState.NoFileAccess, "");
    public static readonly AttachmentPreview PendingGetAccessRequest = new(PreviewAccessState.PendingGetAccessRequest, "");
    public static readonly AttachmentPreview NoPreview = new(PreviewAccessState.Ok, "");
    public static AttachmentPreview Preview(string previewUrl) => new(PreviewAccessState.Ok, previewUrl);
}
