using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing reactions (emoji responses) on chat entries.
/// </summary>
public interface IReactionsBackend : IComputeService, IBackendService
{
    [Obsolete("2026.10: Old pods only, during a rolling update. Returns the latest of ListByAuthor.")]
    [ComputeMethod]
    Task<Reaction?> Get(ChatEntryId entryId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Reaction[]> ListByAuthor(ChatEntryId entryId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ReactionSummary[]> List(ChatEntryId entryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReact(ReactionsBackend_React command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to toggle one emoji reaction of an author on a chat entry.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ReactionsBackend_React(
    [property: DataMember, Key(0)] Reaction Reaction
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Reaction.EntryId.ShardKey;
}
