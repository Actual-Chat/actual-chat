using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public record Attachment(string PreviewUrl, string FileName, string FileType, int? Width, int? Height)
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public int Progress { get; init; }
    public MediaId? MediaId { get; init; }
    public MediaId? ThumbnailMediaId { get; init; }
    [MemberNotNullWhen(true, nameof(MediaId))]
    public bool Uploaded => MediaId != null;
    public bool Failed { get; init; }
    public bool NoAccess { get; init; }

    public AttachmentCleanupCollection Cleanups { get; } = new ();

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";

    public bool IsImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsVideo => MediaTypeExt.IsSupportedVideo(FileType);
    public bool IsUploading => UploadSessionId != "" && !(Uploaded || Failed);
}
