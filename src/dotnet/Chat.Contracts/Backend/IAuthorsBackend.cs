using MemoryPack;

namespace ActualChat.Chat;

public interface IAuthorsBackend : IComputeService
{
    [ComputeMethod]
    Task<AuthorFull?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorFull?> GetByUserId(ChatId chatId, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<UserId>> ListUserIds(ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AuthorFull> OnUpsert(AuthorsBackend_Upsert command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(AuthorsBackend_Remove command, CancellationToken cancellationToken);
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
