namespace ActualChat.Media;

public class MediaLinkPreviews(IServiceProvider services) : IMediaLinkPreviews
{
    private ILinkPreviewsBackend Backend { get; } = services.GetRequiredService<ILinkPreviewsBackend>();

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled()
        => ActualLab.Async.TaskExt.TrueTask;

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => Backend.Get(id, cancellationToken);

    // [ComputeMethod]
    [Obsolete("2023.10: Remains for backward compability")]
    public virtual Task<LinkPreview?> GetForEntry(Symbol id, ChatEntryId entryId, CancellationToken cancellationToken)
        => entryId.IsNone
            ? Task.FromResult<LinkPreview?>(null)
            : Backend.GetForEntry(entryId, cancellationToken);
}
