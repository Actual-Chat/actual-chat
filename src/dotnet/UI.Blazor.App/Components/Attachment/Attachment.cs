using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public record Attachment(string FileName, string FileType, long Length, Size2D Size)
{
    public AttachmentId Id { get; init; } = AttachmentId.New();
    public int Width => Size.Width;
    public int Height => Size.Height;
    public long DurationMs { get; init; }

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";
    // Shared by reference across `with` copies - the cleanup handover between file, source
    // and upload session relies on every copy seeing the same collection
    public AttachmentCleanupCollection Cleanups { get; } = new ();
    // Set for processable images: what the attachment is re-processed from when the preset changes
    public AttachmentSource? Source { get; init; }
    public bool IsProcessing { get; init; }
    public ImageQualityPreset SelectedQuality { get; init; }
    public string Placeholder { get; init; } = "";

    public bool IsSupportedImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsSupportedVideo => MediaTypeExt.IsSupportedVideo(FileType);
    // Reaches the image processor for a placeholder even though GIF can't be re-encoded (below);
    // SVG stays out - it's vector, so the worker has nothing useful to decode
    public bool IsProcessableImage => IsSupportedImage && !MediaTypeExt.IsSvg(FileType);
    // Animated formats lose their animation if re-encoded, so no preset applies to them
    public bool IsReEncodable => IsProcessableImage && !MediaTypeExt.IsGif(FileType);

    public string DemandUploadSessionId()
        => !UploadSessionId.IsNullOrEmpty()
            ? UploadSessionId
            : throw new InvalidOperationException("Upload session not assigned");

    public MetadataBag GetMetadataForUploadSession()
    {
        var metadata = new MetadataBag()
            .Set(nameof(Media.Media.FileName), FileName)
            .Set(nameof(Media.Media.ContentType), FileType)
            .Set(nameof(Media.Media.Length), Length);
        if (IsSupportedImage || IsSupportedVideo)
            metadata = metadata
                .Set(nameof(Media.Media.Width), Size.Width)
                .Set(nameof(Media.Media.Height), Size.Height);
        if (Source is not null && SelectedQuality == ImageQualityPreset.OriginalWithExif)
            metadata = metadata.Set(nameof(Media.Upload.KeepMetadata), true);
        if (!Placeholder.IsNullOrEmpty())
            metadata = metadata.Set(nameof(Media.Media.Placeholder), Placeholder);
        return metadata;
    }
}

public sealed record SourceAttachment(string FileName, string FileType, long Length, FilePreview? Preview)
    : Attachment(FileName, FileType, Length, Preview?.Dimensions ?? default);

public sealed record AttachmentSource(
    IFileProvider FileProvider,
    string FileName,
    string FileType,
    long Length,
    Size2D Size);
