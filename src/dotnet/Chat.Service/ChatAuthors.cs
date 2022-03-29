using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public partial class ChatAuthors : DbServiceBase<ChatDbContext>, IChatAuthors, IChatAuthorsBackend
{
    private const string AuthorIdSuffix = "::authorId";

    private readonly ICommander _commander;
    private readonly IAuth _auth;
    private readonly IUserAuthorsBackend _userAuthorsBackend;
    private readonly IUserAvatarsBackend _userAvatarsBackend;
    private readonly RedisSequenceSet<ChatAuthor> _idSequences;
    private readonly IRandomNameGenerator _randomNameGenerator;
    private readonly IDbEntityResolver<string, DbChatAuthor> _dbChatAuthorResolver;
    private readonly IChatUserSettingsBackend _chatUserSettingsBackend;
    private readonly IUserContactsBackend _userContactsBackend;

    public ChatAuthors(IServiceProvider services) : base(services)
    {
        _commander = services.Commander();
        _auth = Services.GetRequiredService<IAuth>();
        _userAuthorsBackend = services.GetRequiredService<IUserAuthorsBackend>();
        _idSequences = services.GetRequiredService<RedisSequenceSet<ChatAuthor>>();
        _randomNameGenerator = services.GetRequiredService<IRandomNameGenerator>();
        _dbChatAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatAuthor>>();
        _userAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        _chatUserSettingsBackend = services.GetRequiredService<IChatUserSettingsBackend>();
        _userContactsBackend = services.GetRequiredService<IUserContactsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> GetChatAuthor(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await GetByUserId(chatId, user.Id, false, cancellationToken).ConfigureAwait(false);

        var options = await _auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        var authorId = options[chatId + AuthorIdSuffix] as string;
        if (authorId == null)
            return null;
        return await Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<string> GetChatPrincipalId(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var author = await GetChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author.Id;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return user.IsAuthenticated ? user.Id : "";
    }

    // [ComputeMethod]
    public virtual async Task<Author?> GetAuthor(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var chatAuthor = await Get(chatId, authorId, inherit, cancellationToken).ConfigureAwait(false);
        return chatAuthor.ToAuthor();
    }

    // [ComputeMethod]
    public virtual async Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await GetChatIdsByUserId(user.Id, cancellationToken).ConfigureAwait(false);

        var options = await _auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
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
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return null;
        var chatAuthor = await GetChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null)
            return null;
        var avatar = await _userAvatarsBackend.EnsureChatAuthorAvatarCreated(chatAuthor.Id, "", cancellationToken)
            .ConfigureAwait(false);
        return avatar.Id;
    }

    public virtual async Task<bool> CanAddToContacts(Session session, string chatAuthorId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return false;
        if (!ChatAuthor.TryGetChatId(chatAuthorId, out var chatId))
            throw new InvalidOperationException("Invalid chatAuthorId");

        var companion = await Get(chatId, chatAuthorId, false, cancellationToken)
            .ConfigureAwait(false);
        if (companion == null || companion.UserId.IsEmpty)
            return false;
        if (user.Id == companion.UserId)
            return false;
        return !await _userContactsBackend.IsInContactList(user.Id, companion.UserId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<UserContact> AddToContacts(IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatAuthorId) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            throw new InvalidOperationException("No contact list. User is not authenticated.");

        if (!ChatAuthor.TryGetChatId(chatAuthorId, out var chatId))
            throw new InvalidOperationException("Invalid chatAuthorId");
        var companion = await Get(chatId, chatAuthorId, false, cancellationToken)
            .ConfigureAwait(false);
        if (companion == null || companion.UserId.IsEmpty)
            throw new InvalidOperationException("Given chat author is not associated with a user.");

        var createCommand = new IUserContactsBackend.CreateContactCommand(
            new UserContact {
                OwnerUserId = user.Id,
                TargetUserId = companion.UserId,
                Name = companion.Name
            });
        return await _commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
