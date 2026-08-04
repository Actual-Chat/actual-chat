namespace ActualChat.Chat;

/// <summary>
/// Service for managing reactions (emoji responses) to chat messages.
/// </summary>
public interface IReactions : IComputeService
{
    [ComputeMethod]
    Task<Reaction?> Get(Session session, ChatEntryId entryId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ReactionSummary[]> ListSummaries(
        Session session,
        ChatEntryId entryId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReact(Reactions_React command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Reactions_React(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Reaction Reaction
) : ISessionCommand<Unit>, IApiCommand;
