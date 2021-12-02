namespace ActualChat.Chat;

public interface IChatsBackend
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(ChatId chatId, Range<long>? idTileRange, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatTile> GetTile(ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken);
    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetMinId(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetMaxId(ChatId chatId, CancellationToken cancellationToken);
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
