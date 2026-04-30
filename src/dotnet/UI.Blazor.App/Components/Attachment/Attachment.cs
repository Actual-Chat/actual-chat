using System.Collections.Immutable;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Quality preset for image resizing before upload.
/// The int value represents the maximum dimension in pixels.
/// </summary>
public enum ImageQualityPreset
{
    Maximum = 1920,
    Medium = 1280,
    Small = 640,
}

public record Attachment(string FileName, string FileType, long Length, Size Size)
{
    public AttachmentId Id { get; init; } = AttachmentId.New();
    public int Width => Size.Width;
    public int Height => Size.Height;

    public IFileProvider? FileProvider { get; init; }
    public string UploadSessionId { get; init; } = "";
    public AttachmentCleanupCollection Cleanups { get; } = new ();

    public bool IsSupportedImage => MediaTypeExt.IsSupportedImage(FileType);
    public bool IsSupportedVideo => MediaTypeExt.IsSupportedVideo(FileType);
    public bool IsResizableImage => IsSupportedImage && !MediaTypeExt.IsGif(FileType) && !MediaTypeExt.IsSvg(FileType);
    public bool IsUploadPending { get; init; }
    public ImageQualityPreset SelectedQuality { get; init; } = ImageQualityPreset.Maximum;
    public long OriginalLength { get; init; }
    public ImmutableArray<ImageResizeResult>? EstimatedSizes { get; init; }

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
                .Set(nameof(Media.Media.Width), Size.Width)
                .Set(nameof(Media.Media.Height), Size.Height);
        return metadata;
    }
}

public sealed record SourceAttachment(string FileName, string FileType, long Length, FilePreview? Preview)
    : Attachment(FileName, FileType, Length, Preview?.Dimensions ?? default);
