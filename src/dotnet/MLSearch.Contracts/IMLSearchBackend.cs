using ActualLab.Rpc;

namespace ActualChat.MLSearch;

public interface IMLSearchBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<string> GetIndexDocIdByEntryId(ChatEntryId chatEntryId, CancellationToken cancellationToken);
}
