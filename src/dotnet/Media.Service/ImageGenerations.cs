using System.Text;
using ActualChat.AI;
using ActualChat.Hashing;
using ActualChat.Media.Module;
using ActualChat.Uploads;

namespace ActualChat.Media;

// Everything needed to turn a description into stored media: the concurrency cap, the style
// template, and the processor/saver pair. Both the suggestion store and the generic Media_Generate
// command go through this, so the cap is shared rather than duplicated on each path.

public interface IImageGenerations
{
    bool IsAvailable { get; }

    // Returns null when the provider declines the prompt or is not configured - both ordinary outcomes
    Task<MediaId?> Generate(ImageGenerationSpec spec, CancellationToken cancellationToken);
}

public sealed record ImageGenerationSpec(string Scope, string Description, ImageStyle Style)
{
    // Media_RemoveMedia is owner-only, so anything a user is expected to discard later needs this.
    // Null where nothing user-facing owns the result, e.g. the suggestion store's own media.
    public UserId? OwnerId { get; init; }
    public MediaKind MediaKind { get; init; } = MediaKind.ChatPicture;
    // FLUX is trained at 1024 and a direct 512 render is visibly worse; the icon processor caps at
    // 1024 anyway and the image proxy serves whatever smaller size the UI asks for.
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 1024;
    public long? Seed { get; init; }
}

internal sealed class ImageGenerations(IServiceProvider services) : IImageGenerations
{
    // Serializes outbound calls per node: the provider caps the account at 720 req/min and answers
    // 429 past it, and nothing else bounds how many editors generate at once.
    private SemaphoreSlim Concurrency => field ??= new SemaphoreSlim(Settings.MaxConcurrentImageGenerations);

    private IServiceProvider Services { get; } = services;
    private MediaSettings Settings { get; } = services.GetRequiredService<MediaSettings>();

    private ICommander Commander => field ??= Services.Commander();
    private IImageGenerator ImageGenerator => field ??= Services.GetRequiredService<IImageGenerator>();
    private IMediaProcessor MediaProcessor => field ??= Services.GetRequiredService<IMediaProcessor>();
    private IMediaSaver MediaSaver => field ??= Services.GetRequiredService<IMediaSaver>();

    public bool IsAvailable => ImageGenerator.IsAvailable;

    public async Task<MediaId?> Generate(ImageGenerationSpec spec, CancellationToken cancellationToken)
    {
        if (!IsAvailable || spec.Description.IsNullOrEmpty())
            return null;

        var prompt = string.Format(spec.Style.GetPromptTemplate(), spec.Description);
        var request = new ImageGenerationRequest(prompt) {
            Width = spec.Width,
            Height = spec.Height,
            Seed = spec.Seed,
        };

        GeneratedImage? image;
        await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            image = await ImageGenerator.Generate(request, cancellationToken).ConfigureAwait(false);
        }
        finally {
            Concurrency.Release();
        }
        if (image is null)
            return null;

        return await Save(spec, image, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<MediaId> Save(
        ImageGenerationSpec spec,
        GeneratedImage image,
        CancellationToken cancellationToken)
    {
        var extension = MediaTypeExt.GetFileExtension(image.ContentType) ?? ".jpg";
        var uploadedFile = new UploadedStreamFile(
            $"generated{extension}",
            image.ContentType,
            image.Data.Length,
            () => Task.FromResult<Stream>(new MemoryStream(image.Data)));

        // A MediaId is "{scope}:{localId}" and splits on the first ':', so a scope carrying one of
        // its own - a suggestion key, say - cannot be used verbatim.
        var scope = spec.Scope.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
        var mediaId = MediaId.New(scope);
        var isReserved = await Reserve(mediaId, spec, cancellationToken).ConfigureAwait(false);
        using var processed = await MediaProcessor
            .ProcessUpload(uploadedFile, spec.MediaKind, null, cancellationToken)
            .ConfigureAwait(false);
        var mediaRef = await MediaSaver
            .Save(mediaId, processed, isReserved, spec.MediaKind, cancellationToken)
            .ConfigureAwait(false);
        return mediaRef.MediaId;
    }

    // DbMedia.UpdateFrom writes UserId on insert only, so an owned image has to be created owned.
    // This is the shape Media_ReserveMedia uses: reserve the row, then save over it as an update.
    private async Task<bool> Reserve(
        MediaId mediaId,
        ImageGenerationSpec spec,
        CancellationToken cancellationToken)
    {
        if (spec.OwnerId is not { } ownerId)
            return false;

        var media = new MediaFull(mediaId) { UserId = ownerId, Kind = spec.MediaKind };
        var change = new Change<MediaFull> { Create = media };
        await Commander
            .Call(new MediaBackend_Change(mediaId, null, change), true, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
