using MemoryPack;

namespace ActualChat.MLSearch;

public interface IMLSearchBackend: IComputeService
{
    // Commands
    [CommandHandler]
    Task OnStartSearch(MLSearchBackend_Start command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnUpsertIndex(MLSearchBackend_UpsertIndex command, CancellationToken cancellationToken);
}
