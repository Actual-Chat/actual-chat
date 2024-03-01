using MemoryPack;

namespace ActualChat.MLSearch;

public interface IMLSearchBackend: IComputeService
{
    // Commands
    // TODO: this throws a critical error
    //[CommandHandler]
    Task OnStartSearch(MLSearchBackend_Start command, CancellationToken cancellationToken);

    // TODO: this throws a critical error
    //[CommandHandler]
    Task OnUpsertIndex(MLSearchBackend_UpsertIndex command, CancellationToken cancellationToken);
}
