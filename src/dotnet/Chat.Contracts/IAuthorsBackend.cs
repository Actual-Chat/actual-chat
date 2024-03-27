using MemoryPack;

namespace ActualChat.Chat;

public interface IAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<AuthorFull?> Get(ChatId chatId, AuthorId authorId, AuthorsBackend_GetAuthorOption option, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetByUserId(ChatId chatId, UserId userId, AuthorsBackend_GetAuthorOption option, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<UserId>> ListUserIds(ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AuthorFull> OnUpsert(AuthorsBackend_Upsert command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(AuthorsBackend_Remove command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnCopyChat(AuthorsBackend_CopyChat command, CancellationToken cancellationToken);
}

// Commands

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2)] UserId UserId,
    [property: DataMember, MemoryPackOrder(3)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(4)] AuthorDiff Diff,
    [property: DataMember, MemoryPackOrder(5)] bool DoNotNotify = false
) : ICommand<AuthorFull>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_Remove(
    [property: DataMember, MemoryPackOrder(0)] ChatId ByChatId,
    [property: DataMember, MemoryPackOrder(1)] AuthorId ByAuthorId,
    [property: DataMember, MemoryPackOrder(2)] UserId ByUserId
) : ICommand<AuthorFull>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AuthorsBackend_CopyChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId OldChatId,
    [property: DataMember, MemoryPackOrder(1)] ChatId NewChatId,
    [property: DataMember, MemoryPackOrder(2)] (RoleId, RoleId)[] RolesMap,
    [property: DataMember, MemoryPackOrder(3)] string CorrelationId
) : ICommand<bool>, IBackendCommand;

// ReSharper disable once InconsistentNaming
public enum AuthorsBackend_GetAuthorOption
{
    // Gets Author as it is.
    Raw,
    // If chat type supposes creating author entity from multiple instances (e.g. for place chats),
    // then author entity will be build from parts.
    Full
}
