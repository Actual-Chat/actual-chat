using ActualChat.Media.Module;

namespace ActualChat.Media;

internal class MediaLinkPreviews(IServiceProvider services) : IMediaLinkPreviews
{
    private ILinkPreviewsBackend Backend { get; } = services.GetRequiredService<ILinkPreviewsBackend>();
    private MediaSettings Settings { get; } = services.GetRequiredService<MediaSettings>();

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled()
        => Task.FromResult(Settings.EnableLinkPreview);

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => Backend.Get(id, cancellationToken);

    [Obsolete("2023.10: Remaining only for backward compability")]
    // [ComputeMethod]
    public virtual Task<LinkPreview?> GetForEntry(ChatEntryId entryId, CancellationToken cancellationToken)
        => entryId.IsNone
            ? Task.FromResult<LinkPreview?>(null)
            : Backend.GetForEntry(entryId, cancellationToken);
}
