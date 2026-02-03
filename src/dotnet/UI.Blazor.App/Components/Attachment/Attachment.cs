using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public abstract record Attachment(string FileName, string FileType, long Length, int Width, int Height)
{
    public AttachmentId Id { get; init; } = AttachmentId.New();

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";
    public AttachmentCleanupCollection Cleanups { get; } = new ();

    public bool IsImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsVideo => MediaTypeExt.IsSupportedVideo(FileType);

    public string DemandUploadSessionId()
        => !UploadSessionId.IsNullOrEmpty() ? UploadSessionId : throw new InvalidOperationException("Upload session not assigned");
}

public record SourceAttachment(
    string FileName,
    string FileType,
    long Length,
    int Width,
    int Height,
    string PreviewUrl)
    : Attachment(FileName,
        FileType,
        Length,
        Width,
        Height);

// public record AttachExtra(string PreviewUrl);
//
// public record AttachmentWithExtras<TExtra>(
//     string FileName,
//     string FileType,
//     long Length,
//     int Width,
//     int Height,
//     TExtra Extras)
//     : Attachment(FileName,
//         FileType,
//         Length,
//         Width,
//         Height);
