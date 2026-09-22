using ActualChat.WebHooks;
using ActualLab.Rpc;

namespace ActualChat.Chat;

public interface IWebHooksBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<WebHook?> Get(WebHookId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListByScope(WebHookScope scope, string scopeId, CancellationToken cancellationToken);
    // Active outgoing hooks whose scope is the chat or the chat's place
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListActiveForChat(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListActiveForUser(UserId userId, CancellationToken cancellationToken);
    // Every scope, so Settings can show what a user created in chats and places too
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListByCreator(UserId userId, CancellationToken cancellationToken);
    // Incoming hooks are addressed by the SHA-256 of their URL token
    [ComputeMethod]
    Task<WebHook?> GetByTokenHash(string tokenHash, CancellationToken cancellationToken);
    // Cheap gate in front of the per-member scan chat events do for personal "selected chats" hooks
    [ComputeMethod]
    Task<bool> HasUserScopedHooks(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHookDelivery>> ListDeliveries(WebHookId id, int limit, CancellationToken cancellationToken);

    [CommandHandler]
    Task<WebHookChangeResult> OnChange(WebHooksBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnRotateSecret(WebHooksBackend_RotateSecret command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnEnqueue(WebHooksBackend_Enqueue command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRecordDelivery(WebHooksBackend_RecordDelivery command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRecordPost(WebHooksBackend_RecordPost command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDisable(WebHooksBackend_Disable command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRedeliver(WebHooksBackend_Redeliver command, CancellationToken cancellationToken);
    // In-process ping that never touches the outbox
    [CommandHandler]
    Task<WebHookTestResult> OnTest(WebHooksBackend_Test command, CancellationToken cancellationToken);

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnAuthorUpsertedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnAuthorsRemovedEvent(AuthorsRemovedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnPlaceMembershipChangedEvent(PlaceMembershipChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_Change(
    [property: DataMember, Key(0)] WebHookScope Scope,
    [property: DataMember, Key(1)] string ScopeId,
    [property: DataMember, Key(2)] WebHookId? Id,
    [property: DataMember, Key(3)] long? ExpectedVersion,
    [property: DataMember, Key(4)] Change<WebHookDiff> Change,
    [property: DataMember, Key(5)] UserId ChangedBy
) : ICommand<WebHookChangeResult>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => WebHookScopeIds.ToShardKey(Scope, ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_RotateSecret(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId
) : ICommand<string>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_Enqueue(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId,
    [property: DataMember, Key(2)] string DeliveryId,
    [property: DataMember, Key(3)] string EventType,
    [property: DataMember, Key(4)] string Payload
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_RecordDelivery(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId,
    [property: DataMember, Key(2)] string DeliveryId,
    [property: DataMember, Key(3)] WebHookDeliveryStatus Status,
    [property: DataMember, Key(4)] int? StatusCode,
    [property: DataMember, Key(5)] string? Error,
    [property: DataMember, Key(6)] int? LatencyMs,
    [property: DataMember, Key(7)] Moment? NextAttemptAt
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_RecordPost(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_Disable(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId,
    [property: DataMember, Key(2)] WebHookDisabledReason Reason,
    [property: DataMember, Key(3)] string? Error
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_Redeliver(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId,
    [property: DataMember, Key(2)] string DeliveryId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooksBackend_Test(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] string ScopeId,
    [property: DataMember, Key(2)] string SentBy
) : ICommand<WebHookTestResult>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(ScopeId);
}
