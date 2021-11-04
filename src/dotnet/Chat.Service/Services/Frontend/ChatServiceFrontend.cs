using System.Security;

namespace ActualChat.Chat;

internal class ChatServiceFrontend : IChatServiceFrontend
{
    private readonly IChatService _chatService;
    private readonly IAuthService _auth;
    private readonly IAuthorServiceBackend _authorService;
    private readonly ICommander _commander;

    public ChatServiceFrontend(
        IAuthService auth,
        IChatService chatService,
        IAuthorServiceBackend authorService,
        ICommander commander)
    {
        _auth = auth;
        _chatService = chatService;
        _authorService = authorService;
        _commander = commander;
    }

    public virtual async Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatService.TryGet(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session,
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatService.GetEntries(chatId, idRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatService.GetEntryCount(chatId, idRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await _chatService.GetIdRange(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return await _chatService.GetPermissions(chatId, user.Id, cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler]
    public virtual async Task<ChatEntry> CreateEntry(
        IChatServiceFrontend.CreateEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, chatId, text) = command;

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
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
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var chat = new Chat() {
            Title = title,
            OwnerIds = ImmutableArray.Create((UserId)user.Id),
        };

        return await _chatService.CreateChat(new(chat), cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Unit> AssertHasPermissions(
        ChatId chatId,
        UserId userId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var chatPermissions = await _chatService.GetPermissions(chatId, userId, cancellationToken).ConfigureAwait(false);
        if ((chatPermissions & permissions) != permissions)
            throw new SecurityException("Not enough permissions.");
        return default;
    }
}

