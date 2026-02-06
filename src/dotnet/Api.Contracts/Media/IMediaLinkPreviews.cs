namespace ActualChat.Media;

/// <summary>
/// Service for retrieving link preview metadata.
/// </summary>
public interface IMediaLinkPreviews : IComputeService
{
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);
}
