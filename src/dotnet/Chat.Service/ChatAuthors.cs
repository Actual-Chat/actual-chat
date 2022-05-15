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
    public virtual async Task<ChatAuthor?> GetChatAuthor(
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
    public virtual async Task<string> GetChatPrincipalId(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var author = await GetChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author.Id;
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user.IsAuthenticated ? user.Id : "";
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

    // [ComputeMethod]
    public virtual async Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await Backend.GetChatIdsByUserId(user.Id, cancellationToken).ConfigureAwait(false);

        var options = await Auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        var chatIds = options.Items.Keys
            .Select(c => c.Value)
            .Where(c => c.EndsWith(AuthorIdSuffix, StringComparison.Ordinal))
            .Select(c => c.Substring(0, c.Length - AuthorIdSuffix.Length))
            .ToArray();
        return chatIds;
    }

    // [ComputeMethod]
    public virtual async Task<string?> GetChatAuthorAvatarId(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return null;
        var chatAuthor = await GetChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null)
            return null;
        var avatar = await UserAvatarsBackend.EnsureChatAuthorAvatarCreated(chatAuthor.Id, "", cancellationToken)
            .ConfigureAwait(false);
        return avatar.Id;
    }

    public virtual async Task<bool> CanAddToContacts(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        var (userId1, userId2) = await GetPeerChatUserIds(session, chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (userId2.IsNullOrEmpty())
            return false;
        var contact = await UserContactsBackend.GetByTargetId(userId1, userId2, cancellationToken).ConfigureAwait(false);
        return contact == null;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<string>> GetAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return ImmutableArray<string>.Empty;

        return await Backend.GetAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<string>> GetUserIds(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return ImmutableArray<string>.Empty;

        return await Backend.GetUserIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task AddToContacts(IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here
        var (session, chatPrincipalId) = command;
        var (userId1, userId2) = await GetPeerChatUserIds(session, chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (userId2.IsNullOrEmpty())
            return;
        _ = await UserContactsBackend.GetOrCreate(userId1, userId2, cancellationToken).ConfigureAwait(false);
        _ = await UserContactsBackend.GetOrCreate(userId2, userId1, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task CreateChatAuthors(IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var chatUserIds = await Backend.GetUserIds(command.ChatId, cancellationToken).ConfigureAwait(false);
        var existingUserIds = new HashSet<string>(chatUserIds, StringComparer.Ordinal);
        foreach (var userId in command.UserIds) {
            if (existingUserIds.Contains(userId))
                continue;

            var createCommand = new IChatAuthorsBackend.CreateCommand(command.ChatId, userId);
            await Backend.Create(createCommand, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(string, string)> GetPeerChatUserIds(Session session, string chatPrincipalId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return ("", "");
        var userId2 = await Backend.GetUserIdFromPrincipalId(chatPrincipalId, cancellationToken).ConfigureAwait(false);
        if (userId2 == null)
            return ("", "");
        if (user.Id == userId2)
            return ("", "");
        var user2 = await AuthBackend.GetUser(userId2, cancellationToken).ConfigureAwait(false);
        if (user2 == null)
            return ("", "");
        return (user.Id, user2.Id);
    }
}
