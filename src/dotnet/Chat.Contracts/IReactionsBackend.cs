using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IReactionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Reaction?> Get(TextEntryId entryId, AuthorId authorId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<ReactionSummary>> List(TextEntryId entryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReact(ReactionsBackend_React command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ReactionsBackend_React(
    [property: DataMember, MemoryPackOrder(0)] Reaction Reaction
) : ICommand<Unit>, IBackendCommand;
