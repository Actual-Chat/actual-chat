namespace ActualChat.Chat;

public interface IChatServiceFrontend
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session,
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatPermissions> GetPermissions(
        Session session, ChatId chatId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntry> CreateEntry(CreateEntryCommand command, CancellationToken cancellationToken);

    public record CreateChatCommand(Session Session, string Title) : ISessionCommand<Chat>;
    public record CreateEntryCommand(Session Session, ChatId ChatId, string Text) : ISessionCommand<ChatEntry>;
}
