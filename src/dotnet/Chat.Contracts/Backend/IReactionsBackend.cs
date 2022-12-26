namespace ActualChat.Chat;

public interface IReactionsBackend : IComputeService
{
    [ComputeMethod]
    Task<Reaction?> Get(TextEntryId entryId, AuthorId authorId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<ReactionSummary>> List(TextEntryId entryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task React(ReactCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ReactCommand(
        [property: DataMember] Reaction Reaction
    ) : ICommand<Unit>, IBackendCommand;
}
