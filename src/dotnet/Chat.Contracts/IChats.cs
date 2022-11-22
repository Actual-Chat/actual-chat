namespace ActualChat.Chat;

public interface IChats : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<AuthorRules> GetRules(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<ChatSummary?> GetSummary(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    // Client-side methods always skips entries with IsRemoved flag
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken);

    // Client-side methods always skips entries with IsRemoved flag
    [ComputeMethod(MinCacheDuration = 10)]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> HasInvite(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> CanJoin(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ChatEntry?> FindNext(
        Session session,
        ChatId chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Unit> Join(JoinCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Leave(LeaveCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntry> UpsertTextEntry(UpsertTextEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveTextEntry(RemoveTextEntryCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ChatDiff> Change
        ) : ISessionCommand<Chat>;

    [DataContract]
    public sealed record JoinCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record LeaveCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId
    ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record UpsertTextEntryCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] long? LocalId,
        [property: DataMember] string Text,
        [property: DataMember] Option<long?> RepliedChatEntryId = default
        ) : ISessionCommand<ChatEntry>
    {
        [DataMember] public ImmutableArray<TextEntryAttachmentUpload> Attachments { get; set; } =
            ImmutableArray<TextEntryAttachmentUpload>.Empty;
    }

    [DataContract]
    public sealed record RemoveTextEntryCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] long LocalId
        ) : ISessionCommand<Unit>;
}
