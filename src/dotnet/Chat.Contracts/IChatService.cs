namespace ActualChat.Chat;

public interface IChatService
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> TryGet(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(ChatId chatId, Range<long>? idRange, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ImmutableArray<ChatEntry>> GetEntries(ChatId chatId, Range<long> idRange, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatPermissions> GetPermissions(ChatId chatId, UserId userId, CancellationToken cancellationToken);

    [CommandHandler, Internal]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler, Internal]
    Task<ChatEntry> CreateEntry(CreateEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler, Internal]
    Task<ChatEntry> UpdateEntry(UpdateEntryCommand command, CancellationToken cancellationToken);


    public record CreateChatCommand(Chat Chat) : ICommand<Chat>;
    public record CreateEntryCommand(ChatEntry Entry) : ICommand<ChatEntry>;
    public record UpdateEntryCommand(ChatEntry Entry) : ICommand<ChatEntry>;
}
