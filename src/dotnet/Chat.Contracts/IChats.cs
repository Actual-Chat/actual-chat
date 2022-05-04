using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChats
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken);

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

    [ComputeMethod(KeepAliveTime = 1)]
    Task<bool> CheckCanJoin(
        Session session,
        string chatId,
        CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId,
        CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<bool> CanSendUserPeerChatMessage(Session session, string chatAuthorId, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 1)]
    Task<string?> GetUserPeerChatId(Session session, string chatAuthorId, CancellationToken cancellationToken);

    Task<MentionCandidate[]> GetMentionCandidates(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Unit> UpdateChat(UpdateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Unit> JoinChat(JoinChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> CreateTextEntry(CreateTextEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveTextEntry(RemoveTextEntryCommand command, CancellationToken cancellationToken);

    [DataContract]
    public record CreateChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string Title
        ) : ISessionCommand<Chat>
    {
        [DataMember] public bool IsPublic { get; init; }
    }

    [DataContract]
    public record UpdateChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Chat Chat
        ) : ISessionCommand<Unit>;

    [DataContract]
    public record JoinChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public record CreateTextEntryCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] string Text
        ) : ISessionCommand<ChatEntry>
    {
        [DataMember] public ImmutableArray<TextEntryAttachmentUpload> Attachments { get; set; } =
            ImmutableArray<TextEntryAttachmentUpload>.Empty;
    }

    [DataContract]
    public record RemoveTextEntryCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long EntryId
        ) : ISessionCommand<Unit>;
}
