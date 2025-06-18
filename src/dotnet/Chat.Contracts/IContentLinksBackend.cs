using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface IContentLinksBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ContentLinkInfo> GetContentInfo(ContentId contentId, CancellationToken cancellationToken);
}
