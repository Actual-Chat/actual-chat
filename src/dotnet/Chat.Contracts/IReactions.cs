namespace ActualChat.Chat;

public interface IReactions
{
    [ComputeMethod]
    Task<Reaction?> Get(Session session, Symbol chatEntryId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<ReactionSummary>> ListSummaries(
        Session session,
        Symbol chatEntryId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task React(ReactCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ReactCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Reaction Reaction
    ) : ISessionCommand<Unit>;
}
