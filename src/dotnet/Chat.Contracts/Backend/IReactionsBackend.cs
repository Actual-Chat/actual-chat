namespace ActualChat.Chat;

public interface IReactionsBackend : IComputeService
{
    [ComputeMethod]
    Task<Reaction?> Get(string entryId, string authorId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<ReactionSummary>> List(
        string chatEntryId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task React(ReactCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ReactCommand(
        [property: DataMember] Reaction Reaction
    ) : ICommand<Unit>, IBackendCommand;
}
