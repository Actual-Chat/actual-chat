namespace ActualChat.Media;

public class MediaLinkPreviews(IServiceProvider services) : IMediaLinkPreviews
{
    private ILinkPreviewsBackend Backend { get; } = services.GetRequiredService<ILinkPreviewsBackend>();

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => Backend.Get(id, true, cancellationToken);
}
