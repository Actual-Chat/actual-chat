namespace ActualChat.Media;

public interface ILinkPreviews : IComputeService
{
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> IsEnabled();

    [ComputeMethod]
    Task<LinkPreview?> GetForEntry(Symbol id, ChatEntryId entryId, CancellationToken cancellationToken);
}
