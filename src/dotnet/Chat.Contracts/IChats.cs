namespace ActualChat.Chat;

public interface IChats
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<InviteCodeCheckResult> CheckInviteCode(Session session, string inviteCode, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    // Client-side method always skips entries with IsRemoved flag
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatPermissions> GetPermissions(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Unit> UpdateChat(UpdateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Unit> JoinPublicChat(JoinPublicChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> JoinWithInviteCode(JoinWithInviteCodeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> CreateTextEntry(CreateTextEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveTextEntry(RemoveTextEntryCommand command, CancellationToken cancellationToken);

    public record CreateChatCommand(Session Session, string Title) : ISessionCommand<Chat>
    {
        public bool IsPublic { get; init; }
    }

    public record UpdateChatCommand(Session Session, Chat Chat) : ISessionCommand<Unit>;
    public record JoinPublicChatCommand(Session Session, string ChatId) : ISessionCommand<Unit>;
    public record JoinWithInviteCodeCommand(Session Session, string InviteCode) : ISessionCommand<string>;
    public record CreateTextEntryCommand(Session Session, string ChatId, string Text) : ISessionCommand<ChatEntry>;
    public record RemoveTextEntryCommand(Session Session, string ChatId, long EntryId) : ISessionCommand<Unit>;
}
