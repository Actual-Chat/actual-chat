using ActualChat.Chat.Db;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public partial class Chats : DbServiceBase<ChatDbContext>, IChats, IChatsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private readonly IAuth _auth;
    private readonly IAuthBackend _authBackend;
    private readonly IChatAuthors _chatAuthors;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IDbEntityResolver<string, DbChat> _dbChatResolver;
    private readonly RedisSequenceSet<ChatEntry> _idSequences;
    private readonly ICommander _commander;

    public Chats(
        IAuth auth,
        IAuthBackend authBackend,
        IChatAuthors chatAuthors,
        IChatAuthorsBackend chatAuthorsBackend,
        IDbEntityResolver<string, DbChat> dbChatResolver,
        RedisSequenceSet<ChatEntry> idSequences,
        ICommander commander,
        IServiceProvider services) : base(services)
    {
        _auth = auth;
        _authBackend = authBackend;
        _chatAuthors = chatAuthors;
        _chatAuthorsBackend = chatAuthorsBackend;
        _dbChatResolver = dbChatResolver;
        _idSequences = idSequences;
        _commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var author = await _chatAuthors.GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, author?.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        var author = await _chatAuthors.GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, author?.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await GetTile(chatId, idTileRange, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
    {
        var author = await _chatAuthors.GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, author?.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await GetEntryCount(chatId, idTileRange, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var author = await _chatAuthors.GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, author?.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await GetIdRange(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var author = await _chatAuthors.GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        return await GetPermissions(chatId, author?.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ChatEntry> CreateEntry(
        IChats.CreateEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, chatId, text) = command;
        var author = await _chatAuthorsBackend.GetOrCreate(session, chatId, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, author.Id, ChatPermissions.Write, cancellationToken).ConfigureAwait(false);

        var chatEntry = new ChatEntry() {
            ChatId = chatId,
            AuthorId = author.Id,
            Content = text,
            Type = ChatEntryType.Text,
        };
        var upsertCommand = new IChatsBackend.UpsertEntryCommand(chatEntry);
        return await _commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Chat> CreateChat(IChats.CreateChatCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, title) = command;
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var chat = new Chat() {
            Title = title,
            OwnerIds = ImmutableArray.Create((UserId)user.Id),
        };
        var createChatCommand = new IChatsBackend.CreateChatCommand(chat);
        return await _commander.Call(createChatCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
