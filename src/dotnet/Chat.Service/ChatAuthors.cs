using ActualChat.Chat.Db;
using ActualChat.Users;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public partial class ChatAuthors : DbServiceBase<ChatDbContext>, IChatAuthors, IChatAuthorsBackend
{
    private readonly IAuth _auth;
    private readonly IUserInfos _userInfos;
    private readonly IUserStates _userStates;
    private readonly IUserAuthorsBackend _userAuthorsBackend;
    private readonly RedisSequenceSet<ChatAuthor> _idSequences;
    private readonly ICommander _commander;
    private readonly IRandomNameGenerator _randomNameGenerator;

    public ChatAuthors(
        IAuth auth,
        IUserInfos userInfos,
        IUserStates userStates,
        IUserAuthorsBackend userAuthorsBackend,
        RedisSequenceSet<ChatAuthor> idSequences,
        ICommander commander,
        IRandomNameGenerator randomNameGenerator,
        IServiceProvider serviceProvider
    ) : base(serviceProvider)
    {
        _auth = auth;
        _userInfos = userInfos;
        _userStates = userStates;
        _userAuthorsBackend = userAuthorsBackend;
        _idSequences = idSequences;
        _commander = commander;
        _randomNameGenerator = randomNameGenerator;
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> GetSessionChatAuthor(
        Session session, string chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user.IsAuthenticated)
            return await GetByUserId(chatId, user.Id, false, cancellationToken).ConfigureAwait(false);

        var sessionInfo = await _auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var authorId = sessionInfo.Options[$"{chatId}::authorId"] as string;
        if (authorId == null)
            return null;
        return await Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Author?> GetAuthor(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var chatAuthor = await Get(chatId, authorId, inherit, cancellationToken).ConfigureAwait(false);
        return chatAuthor.ToAuthor();
    }
}
