
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
    Task OnChangeRole(Authors_ChangeRole command, CancellationToken cancellationToken);
    [Obsolete("2026.08: Use Authors_ChangeRole. Old clients only.")]
    [CommandHandler]
    Task OnPromoteToOwner(Authors_PromoteToOwner command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_SetAvatar : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required Symbol AvatarId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Invite : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required UserId[] UserIds { get; init; }
    [DataMember(Order = 4), Key(4)] public bool? JoinAnonymously { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Exclude : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Restore : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Leave : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_Join : ApiCommand<AuthorFull>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public Symbol AvatarId { get; init; }
    [DataMember(Order = 4), Key(4)] public bool? JoinAnonymously { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_ChangeRole : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
    [DataMember(Order = 3), Key(3)] public required SystemRole SystemRole { get; init; }
    [DataMember(Order = 4), Key(4)] public required bool IsInRole { get; init; }
}

[DataContract, MessagePackObject]
[Obsolete("2026.08: Use Authors_ChangeRole. Old clients only.")]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_PromoteToOwner : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AuthorId AuthorId { get; init; }
}
