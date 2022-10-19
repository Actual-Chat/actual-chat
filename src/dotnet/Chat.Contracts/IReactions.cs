namespace ActualChat.Chat;

public interface IReactions
{
    [ComputeMethod]
    Task<ImmutableArray<ReactionSummary>> List(
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
