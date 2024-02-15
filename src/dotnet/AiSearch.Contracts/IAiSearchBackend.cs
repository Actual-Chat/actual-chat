using MemoryPack;

namespace ActualChat.AiSearch;

public interface IAiSearchBackend
{
    // Commands
    [CommandHandler]
    Task OnStartSearch(AiSearchBackend_Start command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnUpsertIndex(AiSearchBackend_UpsertIndex command, CancellationToken cancellationToken);
}
