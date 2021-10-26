namespace ActualChat.Chat;

public interface IChatServiceFacade
{
    // Commands
    [CommandHandler]
    Task<Chat> CreateChat(ChatCommands.CreateChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> PostMessage(ChatCommands.PostMessage command, CancellationToken cancellationToken);

    // Queries
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(
        Session session, ChatId chatId, Range<long>? idRange,
        CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(
        Session session, ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session, ChatId chatId, Range<long> idRange,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatPermissions> GetPermissions(
        Session session, ChatId chatId,
        CancellationToken cancellationToken);
}
