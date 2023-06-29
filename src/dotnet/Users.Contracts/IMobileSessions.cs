using MemoryPack;

namespace ActualChat.Users;

public interface IMobileSessions : IComputeService
{
    [CommandHandler]
    Task<string> Create(MobileSessions_Create command, CancellationToken cancellationToken);
}


[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MobileSessions_Create : ICommand<string>, IBackendCommand;


