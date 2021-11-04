using System.Security;

namespace ActualChat.Chat;

internal class ChatServiceFrontend : IChatServiceFrontend
{
    private readonly IChatService _service;
    private readonly IAuth _auth;
    private readonly IAuthorServiceBackend _authorService;
    private readonly ICommander _commander;

    public ChatServiceFrontend(
        IAuth auth,
        IChatService service,
        IAuthorServiceBackend authorService,
        ICommander commander)
    {
        _auth = auth;
        _service = service;
        _authorService = authorService;
        _commander = commander;
    }

    public virtual async Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _service.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session,
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _service.GetEntries(chatId, idRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _service.GetEntryCount(chatId, idRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _service.GetIdRange(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        return await _service.GetPermissions(chatId, user.Id, cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler]
    public virtual async Task<ChatEntry> CreateEntry(
        IChatServiceFrontend.CreateEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, chatId, text) = command;

        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        var authorId = await _authorService.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);

        var chatEntry = new ChatEntry() {
            ChatId = chatId,
            AuthorId = authorId,
            Content = text,
            Type = ChatEntryType.Text,
        };

        return await _commander.Call(
            new IChatService.CreateEntryCommand(chatEntry),
            isolate: true,
            cancellationToken
            ).ConfigureAwait(false);
    }

    [CommandHandler]
    public virtual async Task<Chat> CreateChat(IChatServiceFrontend.CreateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, title) = command;
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var chat = new Chat() {
            Title = title,
            OwnerIds = ImmutableArray.Create((UserId)user.Id),
        };

        return await _service.CreateChat(new(chat), cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Unit> AssertHasPermissions(
        ChatId chatId,
        UserId userId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var chatPermissions = await _service.GetPermissions(chatId, userId, cancellationToken).ConfigureAwait(false);
        if ((chatPermissions & permissions) != permissions)
            throw new SecurityException("Not enough permissions.");
        return default;
    }
}

