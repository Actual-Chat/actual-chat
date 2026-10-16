using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service storing join requests; who may decide them is checked by <see cref="IJoinRequests"/>.
/// </summary>
public interface IJoinRequestsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<JoinRequest?> Get(ChatId chatId, JoinRequestId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<JoinRequest?> GetLatest(ChatId chatId, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<JoinRequest>> ListPending(ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<JoinRequest> OnCreate(JoinRequestsBackend_Create command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<JoinRequest> OnChangeStatus(JoinRequestsBackend_ChangeStatus command, CancellationToken cancellationToken);

    [EventHandler]
    Task OnJoinRequestChangedEvent(JoinRequestChangedEvent eventCommand, CancellationToken cancellationToken);
}

// Returns the open request the user already has for this chat instead of a second one

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record JoinRequestsBackend_Create(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] JoinRequestId? ParentId = null
) : ICommand<JoinRequest>, IBackendCommand, IHasShardKey
{
    [DataMember, Key(3)] public Symbol InviteId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record JoinRequestsBackend_ChangeStatus(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] JoinRequestId Id,
    [property: DataMember, Key(2)] JoinRequestStatus Status,
    [property: DataMember, Key(3)] UserId? ActorId = null
) : ICommand<JoinRequest>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}
