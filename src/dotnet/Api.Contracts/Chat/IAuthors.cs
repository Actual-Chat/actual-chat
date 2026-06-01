using ActualChat.Users;

namespace ActualChat.Chat;

/// <summary>
/// Service for managing chat authors and membership.
/// </summary>
public interface IAuthors : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Author?> Get(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<AuthorFull?> GetOwn(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetFull(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Account?> GetAccount(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Author?> GetByUserId(Session session, ChatId chatId, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Presence> GetPresence(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiNullable8<Moment>> GetLastCheckIn(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<AuthorId[]> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId[]> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<AuthorFull> OnJoin(Authors_Join command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnLeave(Authors_Leave command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnInvite(Authors_Invite command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnExclude(Authors_Exclude command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRestore(Authors_Restore command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetAvatar(Authors_SetAvatar command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPromoteToOwner(Authors_PromoteToOwner command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_SetAvatar(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Symbol AvatarId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Invite(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] UserId[] UserIds,
    [property: DataMember, MemoryPackOrder(3), Key(3)] bool? JoinAnonymously = null
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Exclude(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Restore(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Leave(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Join(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Symbol AvatarId = default,
    [property: DataMember, MemoryPackOrder(3), Key(3)] bool? JoinAnonymously = null
) : ISessionCommand<AuthorFull>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_PromoteToOwner(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;
