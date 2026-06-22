namespace ActualChat.UI.Blazor.App.Components;

public sealed record AttachmentProgress(double Progress, string Details = "")
{
    public static readonly AttachmentProgress New = new(0);

    public bool IsInProgress { get; init; }
    public bool IsReady { get; init; }
    public bool IsFailed { get; init; }
    public long UploadProgress { get; init; }
    public long UploadSize { get; init; }
}
