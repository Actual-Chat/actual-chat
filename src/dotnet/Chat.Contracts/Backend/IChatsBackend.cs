namespace ActualChat.Chat;

public interface IChatsBackend
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(ChatId chatId, Range<long>? idRange, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ImmutableArray<ChatEntry>> GetEntries(ChatId chatId, Range<long> idRange, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatPermissions> GetPermissions(ChatId chatId, AuthorId? authorId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> UpsertEntry(UpsertEntryCommand command, CancellationToken cancellationToken);

    public record CreateChatCommand(Chat Chat) : ICommand<Chat>, IBackendCommand;
    public record UpsertEntryCommand(ChatEntry Entry) : ICommand<ChatEntry>, IBackendCommand;
}
