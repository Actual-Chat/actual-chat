using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public record Attachment(string FileName, string FileType, long Length, int Width, int Height)
{
    public AttachmentId Id { get; init; } = AttachmentId.New();

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";
    public AttachmentCleanupCollection Cleanups { get; } = new ();

    public bool IsSupportedImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsSupportedVideo => MediaTypeExt.IsSupportedVideo(FileType);

    public string DemandUploadSessionId()
        => !UploadSessionId.IsNullOrEmpty() ? UploadSessionId : throw new InvalidOperationException("Upload session not assigned");

    public PropertyBag GetMetadataForUploadSession()
    {
        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), FileName)
            .Set(nameof(Media.Media.ContentType), FileType)
            .Set(nameof(Media.Media.Length), Length);
        if (IsSupportedImage || IsSupportedVideo)
            metadata = metadata
                .Set(nameof(Media.Media.Width), Width)
                .Set(nameof(Media.Media.Height), Height);
        return metadata;
    }
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
