using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] AuthorId? AuthorId,
    [property: DataMember, MemoryPackOrder(2)] UserId? UserId,
    [property: DataMember, MemoryPackOrder(3)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(4)] AuthorDiff Diff,
    [property: DataMember, MemoryPackOrder(5)] bool DoNotNotify = false
) : ICommand<AuthorFull>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Remove(
    [property: DataMember, MemoryPackOrder(0)] ChatId? ByChatId,
    [property: DataMember, MemoryPackOrder(1)] AuthorId? ByAuthorId,
    [property: DataMember, MemoryPackOrder(2)] UserId? ByUserId
) : ICommand<AuthorFull>, IBackendCommand, IHasShardKey<PrincipalId?>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public PrincipalId? ShardKey => (ByChatId is not null, ByAuthorId is not null, ByUserId is not null) switch {
        (true, _, _) => AuthorId.New(ByChatId!, 1),
        (_, true, _) => ByAuthorId,
        (_, _, true) => ByUserId,
        _ => null,
    };
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_CopyChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId OldChatId,
    [property: DataMember, MemoryPackOrder(1)] ChatId NewChatId,
    [property: DataMember, MemoryPackOrder(2)] (RoleId, RoleId)[] RolesMap,
    [property: DataMember, MemoryPackOrder(3)] string CorrelationId
) : ICommand<bool>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => OldChatId;
}

// ReSharper disable once InconsistentNaming
