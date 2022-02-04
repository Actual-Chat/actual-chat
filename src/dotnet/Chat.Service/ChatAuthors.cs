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
}
