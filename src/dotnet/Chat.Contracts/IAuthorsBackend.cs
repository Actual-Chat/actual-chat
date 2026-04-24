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
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId? AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] UserId? UserId,
    [property: DataMember, MemoryPackOrder(3), Key(3)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(4), Key(4)] AuthorDiff Diff,
    [property: DataMember, MemoryPackOrder(5), Key(5)] bool DoNotNotify = false
) : ICommand<AuthorFull>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Command to remove a chat author by chat ID, author ID, or user ID.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Remove(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId? ByChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId? ByAuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] UserId? ByUserId
) : ICommand<AuthorFull>, IBackendCommand, IHasShardKey<PrincipalId?>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
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
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_CopyChat(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId OldChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId NewChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] (RoleId, RoleId)[] RolesMap,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string CorrelationId
) : ICommand<bool>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => OldChatId;
}

// ReSharper disable once InconsistentNaming
