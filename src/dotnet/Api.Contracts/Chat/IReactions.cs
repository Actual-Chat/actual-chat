using MemoryPack;

namespace ActualChat.Chat;

public interface IReactions : IComputeService
{
    [ComputeMethod]
    Task<Reaction?> Get(Session session, TextEntryId entryId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<ReactionSummary>> ListSummaries(
        Session session,
        TextEntryId entryId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReact(Reactions_React command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Reactions_React(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Reaction Reaction
) : ISessionCommand<Unit>;
