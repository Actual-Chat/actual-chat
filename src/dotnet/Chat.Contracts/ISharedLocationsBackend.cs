using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for locations shared into a chat (live or frozen one-shot).
/// </summary>
public interface ISharedLocationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<SharedLocation?> Get(SharedLocationId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<SharedLocation>> ListLive(ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<SharedLocation?> OnChange(SharedLocationsBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SharedLocationsBackend_Change(
    [property: DataMember, Key(0)] SharedLocationId? Id,
    [property: DataMember, Key(1)] AuthorId AuthorId,
    [property: DataMember, Key(2)] Change<SharedLocationDiff> Change
) : ICommand<SharedLocation?>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => AuthorId.ChatId;
}
