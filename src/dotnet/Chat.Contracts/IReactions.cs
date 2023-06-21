namespace ActualChat.Chat;

public interface IReactions : IComputeService
{
    [ComputeMethod]
    Task<Reaction?> Get(Session session, TextEntryId entryId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<ReactionSummary>> ListSummaries(
        Session session,
        TextEntryId entryId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReact(Reactions_React command, CancellationToken cancellationToken);
}

[DataContract]
// ReSharper disable once InconsistentNaming
public sealed partial record Reactions_React(
    [property: DataMember] Session Session,
    [property: DataMember] Reaction Reaction
) : ISessionCommand<Unit>;
