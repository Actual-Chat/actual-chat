using ActualChat.UI.Blazor.App.Module;
using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Runs an attachment image through the JS image processor (resize, jpegli, metadata strip) and
/// returns a provider for the result; <c>null</c> means the source should be uploaded as-is.
/// </summary>
public sealed class ImageAttachmentProcessor(IServiceProvider services)
{
    private static readonly string JSProcessUrlMethod
        = $"{BlazorUIAppModule.ImportName}.ImageProcessingInterop.processUrl";

    private IServiceProvider Services { get; } = services;
    private IJSRuntime JS => field ??= Services.JSRuntime();
    private IProcessedImageStore ProcessedImageStore => field ??= Services.GetRequiredService<IProcessedImageStore>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<ImageProcessingResult?> Process(
        IFileProvider source,
        Size2D sourceSize,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        var request = preset.ToRequest();
        try {
            return source switch {
                WebFileProvider webSource
                    => await ProcessWeb(webSource, request, sourceSize, preset, cancellationToken).ConfigureAwait(false),
                MauiFileProvider mauiSource
                    => await ProcessMaui(mauiSource, request, sourceSize, preset, cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e,
                "Failed to process image '{FileName}' with {Preset}, uploading the source instead",
                source.Metadata.FileName, preset);
            return null;
        }
    }

    // Private methods

    private async Task<ImageProcessingResult> ProcessWeb(
        WebFileProvider source,
        ImageProcessRequest request,
        Size2D sourceSize,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        var image = await source.ProcessImage(request, cancellationToken).ConfigureAwait(false);
        if (image.IsSource || image.FileProvider is null)
            return CreateResult(null, image, sourceSize, preset);

        var provider = new WebFileProvider {
            Metadata = CreateMetadata(source.Metadata, image),
            WebFileProviderInternal = new WebFileProviderInternal(
                image.FileProvider, image.PreviewUrl, false, Task.FromResult(true)),
        };
        provider.Initialize(Services);
        return CreateResult(provider, image, sourceSize, preset);
    }

    private async Task<ImageProcessingResult> ProcessMaui(
        MauiFileProvider source,
        ImageProcessRequest request,
        Size2D sourceSize,
        ImageQualityPreset preset,
        CancellationToken cancellationToken)
    {
        var url = await source.GetContentUrl(preset.GetMaxSize(), cancellationToken).ConfigureAwait(false);
        var image = await JS
            .InvokeAsync<ProcessedStreamImage>(JSProcessUrlMethod, cancellationToken, url, request)
            .ConfigureAwait(false);
        if (image.IsSource || image.Stream is null)
            return CreateResult(null, image, sourceSize, preset);

        await using var __ = image.Stream.ConfigureAwait(false);
        var stream = await image.Stream
            .OpenReadStreamAsync(Constants.Attachments.FileSizeLimit, cancellationToken)
            .ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var metadata = CreateMetadata(source.Metadata, image);
        var provider = await ProcessedImageStore.Save(stream, metadata, cancellationToken).ConfigureAwait(false);
        return CreateResult(provider, image, sourceSize, preset);
    }

    private static FileMetadata CreateMetadata(FileMetadata source, ProcessedImage image)
    {
        var extension = image.MimeType switch {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => null,
        };
        if (extension is null)
            return new FileMetadata { FileName = source.FileName, FileType = source.FileType, Length = image.Size };

        return new FileMetadata {
            FileName = ((FilePath)source.FileName).ChangeExtension(extension),
            FileType = image.MimeType,
            Length = image.Size,
        };
    }

    private static ImageProcessingResult CreateResult(
        IFileProvider? provider,
        ProcessedImage image,
        Size2D sourceSize,
        ImageQualityPreset preset)
    {
        var size = image.Width > 0 && image.Height > 0 ? new Size2D(image.Width, image.Height) : sourceSize;
        var hasEstimate = image.EstimateSizes.Length > 0;
        var sizeEstimate = preset switch {
            ImageQualityPreset.Uhd4K when hasEstimate => new ImageSizeEstimate(image.Size, image.EstimateSizes[0]),
            ImageQualityPreset.FullHd when hasEstimate => new ImageSizeEstimate(image.EstimateSizes[0], image.Size),
            _ => null,
        };
        return new ImageProcessingResult(provider, size, sizeEstimate);
    }
}
