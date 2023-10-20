namespace ActualChat.Media;

public interface IMediaLinkPreviews : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled();
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LinkPreview?> GetForEntry(Symbol id, ChatEntryId entryId, CancellationToken cancellationToken);
}
