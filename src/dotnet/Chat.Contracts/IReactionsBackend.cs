using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing reactions (emoji responses) on chat entries.
/// </summary>
public interface IReactionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Reaction?> Get(TextEntryId entryId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ReactionSummary[]> List(TextEntryId entryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReact(ReactionsBackend_React command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to add or remove a reaction on a chat entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ReactionsBackend_React(
    [property: DataMember, MemoryPackOrder(0)] Reaction Reaction
) : ICommand<Unit>, IBackendCommand, IHasShardKey<TextEntryId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public TextEntryId ShardKey => Reaction.EntryId;
}
