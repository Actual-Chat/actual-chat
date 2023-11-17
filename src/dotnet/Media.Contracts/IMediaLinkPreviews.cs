namespace ActualChat.Media;

public interface IMediaLinkPreviews : IComputeService
{
    [ComputeMethod]
    [Obsolete("2023.11: It's always true now.")]
    Task<bool> IsEnabled();
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);
    [Obsolete("2023.10: Remaining only for backward compability")]
    [ComputeMethod]
    Task<LinkPreview?> GetForEntry(Symbol id, ChatEntryId entryId, CancellationToken cancellationToken);
}
