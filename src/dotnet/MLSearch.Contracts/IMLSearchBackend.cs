using MemoryPack;

namespace ActualChat.MLSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearchBackend_Start(
);

public interface IMLSearchBackend: IComputeService
{
    // Commands
    // TODO: this throws a critical error
    //[CommandHandler]
    Task OnStartSearch(MLSearchBackend_Start command, CancellationToken cancellationToken);
}
