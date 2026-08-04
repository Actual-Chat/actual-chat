using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing chat authors (participants).
/// </summary>
public interface IAuthorsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<AuthorFull?> Get(ChatId chatId, AuthorId authorId, RequestedAuthorKind authorKind, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetByUserId(ChatId chatId, UserId userId, RequestedAuthorKind authorKind, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId[]> ListUserIds(ChatId chatId, CancellationToken cancellationToken);
    // Not a [ComputeMethod]!
    Task<AuthorFull[]> ListChanged(
        ChangedAuthorsQuery query,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<AuthorFull> OnUpsert(AuthorsBackend_Upsert command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(AuthorsBackend_Remove command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnCopyChat(AuthorsBackend_CopyChat command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnAvatarChangedEvent(AvatarChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnAuthorLeftPlaceEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken);
}

// Commands

/// <summary>
/// Command to create or update a chat author.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Upsert(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] AuthorId? AuthorId,
    [property: DataMember, Key(2)] UserId? UserId,
    [property: DataMember, Key(3)] long? ExpectedVersion,
    [property: DataMember, Key(4)] AuthorDiff Diff,
    [property: DataMember, Key(5)] bool DoNotNotify = false
) : ICommand<AuthorFull>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Command to remove a chat author by chat ID, author ID, or user ID.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Remove(
    [property: DataMember, Key(0)] ChatId? ByChatId,
    [property: DataMember, Key(1)] AuthorId? ByAuthorId,
    [property: DataMember, Key(2)] UserId? ByUserId
) : ICommand<AuthorFull>, IBackendCommand, IHasShardKey<PrincipalId?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public PrincipalId? ShardKey => (ByChatId is not null, ByAuthorId is not null, ByUserId is not null) switch {
        (true, _, _) => AuthorId.New(ByChatId!, 1),
        (_, true, _) => ByAuthorId,
        (_, _, true) => ByUserId,
        _ => null,
    };
}

/// <summary>
/// Command to copy authors from one chat to another.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_CopyChat(
    [property: DataMember, Key(0)] ChatId OldChatId,
    [property: DataMember, Key(1)] ChatId NewChatId,
    [property: DataMember, Key(2)] (RoleId, RoleId)[] RolesMap,
    [property: DataMember, Key(3)] string CorrelationId
) : ICommand<bool>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => OldChatId;
}

// ReSharper disable once InconsistentNaming
