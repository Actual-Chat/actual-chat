using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public record Attachment(string Id, string PreviewUrl, string FileName, string FileType)
{
    public int Progress { get; init; }
    public MediaId? MediaId { get; init; }
    public MediaId? ThumbnailMediaId { get; init; }
    [MemberNotNullWhen(true, nameof(MediaId))]
    public bool Uploaded => MediaId != null;
    public bool Failed { get; init; }

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";

    public bool IsImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsVideo => MediaTypeExt.IsSupportedVideo(FileType);
}
