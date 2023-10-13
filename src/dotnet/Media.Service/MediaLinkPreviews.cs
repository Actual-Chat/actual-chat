using ActualChat.Chat;
using ActualChat.Media.Module;

namespace ActualChat.Media;

internal class MediaLinkPreviews(IServiceProvider services) : IMediaLinkPreviews
{
    private ILinkPreviewsBackend Backend { get; } = services.GetRequiredService<ILinkPreviewsBackend>();
    private MediaSettings Settings { get; } = services.GetRequiredService<MediaSettings>();

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => Backend.Get(id, cancellationToken);

    // [ComputeMethod]
    public virtual Task<LinkPreview?> GetForEntry(Symbol id, ChatEntryId entryId, CancellationToken cancellationToken)
        => id.IsEmpty || entryId.IsNone
            ? Task.FromResult<LinkPreview?>(null)
            : Backend.GetForEntry(id, entryId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled()
        => Task.FromResult(Settings.EnableLinkPreview);

}
