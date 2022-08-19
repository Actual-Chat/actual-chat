using ActualChat.Users;

namespace ActualChat.Chat;

public interface IChats : IComputeService
{
    [ComputeMethod]
    Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Chat>> List(Session session, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod]
    Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Range<long>> GetLastIdTile0(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Range<long>> GetLastIdTile1(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    // Client-side method always skips entries with IsRemoved flag
    [ComputeMethod]
    Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> CanJoin(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> CanSendPeerChatMessage(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string?> GetPeerChatId(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<UserContact?> GetPeerChatContact(Session session, Symbol chatId, CancellationToken cancellationToken);

    Task<ImmutableArray<MentionCandidate>> ListMentionCandidates(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat?> ChangeChat(ChangeChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Unit> JoinChat(JoinChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task LeaveChat(LeaveChatCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntry> UpsertTextEntry(UpsertTextEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveTextEntry(RemoveTextEntryCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ChatDiff> Change
        ) : ISessionCommand<Chat?>;

    [DataContract]
    public sealed record JoinChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record UpsertTextEntryCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long? Id,
        [property: DataMember] string Text) : ISessionCommand<ChatEntry>
    {
        [DataMember] public ImmutableArray<TextEntryAttachmentUpload> Attachments { get; set; } =
            ImmutableArray<TextEntryAttachmentUpload>.Empty;
        [DataMember] public long? RepliedChatEntryId { get; set; }
    }

    [DataContract]
    public sealed record RemoveTextEntryCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long EntryId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record LeaveChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
    ) : ISessionCommand<Unit>;
}
