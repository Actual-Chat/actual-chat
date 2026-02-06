using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for resolving content links to their metadata.
/// </summary>
public interface IContentLinksBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ContentLinkInfo> GetContentInfo(ContentId contentId, CancellationToken cancellationToken);
}
