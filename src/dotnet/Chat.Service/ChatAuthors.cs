using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatAuthors : DbServiceBase<ChatDbContext>, IChatAuthors
{
    private const string AuthorIdSuffix = "::authorId";
    private IChatAuthorsBackend? _backend;

    private ICommander Commander { get; }
    private IAuth Auth { get; }
    private IAuthBackend AuthBackend { get; }
    private IUserAvatarsBackend UserAvatarsBackend { get; }
    private IUserContactsBackend UserContactsBackend { get; }
    private IUserPresences UserPresences { get; }
    private IChatAuthorsBackend Backend => _backend ??= Services.GetRequiredService<IChatAuthorsBackend>();

    public ChatAuthors(IServiceProvider services) : base(services)
    {
        Commander = services.Commander();
        Auth = Services.GetRequiredService<IAuth>();
        AuthBackend = Services.GetRequiredService<IAuthBackend>();
        UserAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        UserContactsBackend = services.GetRequiredService<IUserContactsBackend>();
        UserPresences = services.GetRequiredService<IUserPresences>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> GetOwnAuthor(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await Backend.GetByUserId(chatId, user.Id, false, cancellationToken).ConfigureAwait(false);

        var options = await Auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        var authorId = options[chatId + AuthorIdSuffix] as string;
        if (authorId == null)
            return null;
        return await Backend.Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Symbol> GetOwnPrincipalId(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var author = await GetOwnAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author.Id;
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user.IsAuthenticated ? user.Id : "";
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return ImmutableArray<Symbol>.Empty;

        return await Backend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return ImmutableArray<Symbol>.Empty;

        return await Backend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await Backend.ListUserChatIds(user.Id, cancellationToken).ConfigureAwait(false);

        var options = await Auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        var chatIds = options.Items.Keys
            .Select(c => c.Value)
            .Where(c => c.OrdinalEndsWith(AuthorIdSuffix))
            .Select(c => new Symbol(c.Substring(0, c.Length - AuthorIdSuffix.Length)))
            .ToImmutableArray();
        return chatIds;
    }

    // [ComputeMethod]
    public virtual async Task<Author?> GetAuthor(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var chatAuthor = await Backend.Get(chatId, authorId, inherit, cancellationToken).ConfigureAwait(false);
        return chatAuthor.ToAuthor();
    }


    // [ComputeMethod]
    public virtual async Task<Presence> GetAuthorPresence(
        string chatId, string authorId,
        CancellationToken cancellationToken)
    {
        var chatAuthor = await Backend.Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null)
            return Presence.Offline;
        if (chatAuthor.UserId.IsEmpty || chatAuthor.IsAnonymous)
            return Presence.Unknown; // Important: we shouldn't report anonymous author presence
        return await UserPresences.Get(chatAuthor.UserId.Value, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> CanAddToContacts(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        var (userId, otherUserId) = await GetPeerChatUserIds(session, chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return false;
        var contact = await UserContactsBackend.Get(userId, otherUserId, cancellationToken).ConfigureAwait(false);
        return contact == null;
    }

    // [CommandHandler]
    public virtual async Task AddToContacts(IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here
        var (session, chatPrincipalId) = command;
        var (userId, otherUserId) = await GetPeerChatUserIds(session, chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return;
        _ = await UserContactsBackend.GetOrCreate(userId, otherUserId, cancellationToken).ConfigureAwait(false);
        _ = await UserContactsBackend.GetOrCreate(otherUserId, userId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task CreateChatAuthors(IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var chatUserIds = await Backend.ListUserIds(command.ChatId, cancellationToken).ConfigureAwait(false);
        var existingUserIds = new HashSet<Symbol>(chatUserIds);
        foreach (var userId in command.UserIds) {
            if (existingUserIds.Contains(userId))
                continue;

            var createCommand = new IChatAuthorsBackend.CreateCommand(command.ChatId, userId);
            await Commander.Call(createCommand, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task<(Symbol UserId, Symbol OtherUserId)> GetPeerChatUserIds(
        Session session, string chatPrincipalId,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return default;

        var otherUserId = await Backend.GetUserId(chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (otherUserId.IsEmpty)
            return default;

        if (user.Id == otherUserId)
            return default;

        var otherUser = await AuthBackend.GetUser(otherUserId, cancellationToken).ConfigureAwait(false);
        if (otherUser == null)
            return default;

        return (user.Id, otherUser.Id);
    }
}
