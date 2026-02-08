using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed record Attachment(string FileName, string FileType, long Length, int Width, int Height, Task<bool> WhenFilePermissionGranted, Task<string> GetPreviewUrl)
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public int Progress { get; init; }
    public MediaId? MediaId { get; init; }
    public MediaId? ThumbnailMediaId { get; init; }
    [MemberNotNullWhen(true, nameof(MediaId))]
    public bool Uploaded => MediaId != null;
    public bool Failed { get; init; }
    public bool NoAccess { get; init; }
    public string PreviewUrl {
        get {
            if (!GetPreviewUrl.IsCompleted)
                throw StandardError.Constraint("Preview not yet resolved.");

            if (!GetPreviewUrl.IsCompletedSuccessfully)
                return "";
            return GetPreviewUrl.GetAwaiter().GetResult();
        }
    }

    public AttachmentCleanupCollection Cleanups { get; } = new ();

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";

    public bool IsImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsVideo => MediaTypeExt.IsSupportedVideo(FileType);
    public bool IsUploading => !UploadSessionId.IsNullOrEmpty() && !(Uploaded || Failed);
}
