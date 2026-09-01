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
public sealed partial record Reactions_React : ApiCommand<Unit>, IQueuedCommand
{
    [DataMember(Order = 2), Key(2)] public required Reaction Reaction { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string PartitionKey
        // Coalescing is left at None: OnReact toggles, so collapsing two clicks would flip the outcome
        => Reaction.EntryId.Value;
}
