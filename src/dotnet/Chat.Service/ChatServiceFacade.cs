using System.Security;
using ActualChat.Users;

namespace ActualChat.Chat;

public class ChatServiceFacade : IChatServiceFacade
{
    private readonly ICommander _commander;
    private readonly IChatService _chatService;
    private readonly IAuthService _auth;
    private readonly IAuthorService _authorService;

    public ChatServiceFacade(
        IAuthService auth,
        IChatService chatService,
        IAuthorService authorService,
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
        IChatServiceFacade.CreateEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var (session, chatId, text) = command;

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        var authorId = await GetAuthorIdFromSession(session, chatId, cancellationToken).ConfigureAwait(false)
            ?? await GetOrAddAuthorIdFromDatabase(session, chatId, cancellationToken).ConfigureAwait(false);

        var chatEntry = new ChatEntry() {
            ChatId = chatId,
            AuthorId = authorId,
            Content = text,
            Type = ChatEntryType.Text,
        };

        return await _chatService.CreateEntry(new(chatEntry), cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler]
    public virtual async Task<Chat> CreateChat(IChatServiceFacade.CreateChatCommand command, CancellationToken cancellationToken)
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

    // TODO: move this under an abstraction like IAuthorIdAccessor
    private async Task<string?> GetAuthorIdFromSession(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var sessionInfo = await _auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo.Options[$"{chatId}::authorId"] as string;
    }

    private async Task<string> GetOrAddAuthorIdFromDatabase(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var author = await _authorService.GetByUserIdAndChatId(user.Id, chatId, cancellationToken).ConfigureAwait(false);
        var authorId = author?.AuthorId ?? AuthorId.None;
        var userId = user.IsAuthenticated ? user.Id : UserId.None;
        if (authorId.IsNone) {
            authorId = await _authorService.CreateAuthor(new(userId, chatId), cancellationToken).ConfigureAwait(false);
        }
        await _commander.Call(
            new ISessionInfoService.UpsertCommand(session, new($"{chatId}::authorId", authorId)),
            isolate: true,
            cancellationToken
        ).ConfigureAwait(false);
        return authorId.ToString();
    }
}

