using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public class ChatAuthorsBackend : DbServiceBase<ChatDbContext>, IChatAuthorsBackend
{
    private readonly ThreadSafeLruCache<Symbol, long> _maxLocalIdCache = new(16384);

    private const string AuthorIdSuffix = "::authorId";
    private IChatAuthors? _frontend;

    private ICommander Commander { get; }
    private IAuth Auth { get; }
    private IUserProfilesBackend UserProfilesBackend { get; }
    private IUserAvatarsBackend UserAvatarsBackend { get; }
    private RedisSequenceSet<ChatAuthor> IdSequences { get; }
    private IRandomNameGenerator RandomNameGenerator { get; }
    private IDbEntityResolver<string, DbChatAuthor> DbChatAuthorResolver { get; }
    private IChatUserSettingsBackend ChatUserSettingsBackend { get; }
    private IChatAuthors Frontend => _frontend ??= Services.GetRequiredService<IChatAuthors>();

    public ChatAuthorsBackend(IServiceProvider services) : base(services)
    {
        Commander = services.Commander();
        Auth = services.GetRequiredService<IAuth>();
        UserProfilesBackend = services.GetRequiredService<IUserProfilesBackend>();
        IdSequences = services.GetRequiredService<RedisSequenceSet<ChatAuthor>>();
        RandomNameGenerator = services.GetRequiredService<IRandomNameGenerator>();
        DbChatAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatAuthor>>();
        UserAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        ChatUserSettingsBackend = services.GetRequiredService<IChatUserSettingsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> Get(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var dbChatAuthor = await DbChatAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(dbChatAuthor?.ChatId, chatId))
            return null;
        var chatAuthor = dbChatAuthor.ToModel();
        return await InheritFromUserAuthor(chatAuthor, inherit, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> GetByUserId(
        string chatId, string userId, bool inherit,
        CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty() || chatId.IsNullOrEmpty())
            return null;

        ChatAuthor? chatAuthor;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChatAuthor = await dbContext.ChatAuthors
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            chatAuthor = dbChatAuthor?.ToModel();
        }

        return await InheritFromUserAuthor(chatAuthor, inherit, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<string[]> GetChatIdsByUserId(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return Array.Empty<string>();

        string[] chatIds;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            chatIds = await dbContext.ChatAuthors
                .Where(a => a.UserId == userId)
                .Select(a => a.ChatId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        return chatIds;
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chatAuthor = await Frontend.GetChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userId = user.IsAuthenticated ? user.Id : Symbol.Empty;

        var createAuthorCommand = new IChatAuthorsBackend.CreateCommand(chatId, userId);
        chatAuthor = await Commander.Call(createAuthorCommand, true, cancellationToken).ConfigureAwait(false);

        if (!user.IsAuthenticated) {
            var updateOptionCommand = new ISessionOptionsBackend.UpsertCommand(
                session,
                new(chatId + AuthorIdSuffix, chatAuthor.Id));
            await Commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return chatAuthor;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<string>> GetAuthorIds(string chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty())
            return ImmutableArray<string>.Empty;

        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);
        var authorIds = await dbContext.ChatAuthors
            .Where(a => a.ChatId == chatId)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return ImmutableArray.Create(authorIds);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<string>> GetUserIds(string chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty())
            return ImmutableArray<string>.Empty;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var userIds = await dbContext.ChatAuthors
            .Where(a => a.ChatId == chatId && a.UserId != null)
            .Select(a => a.UserId!)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return ImmutableArray.Create(userIds);
    }

    // [CommandHandler]
    public virtual async Task<ChatAuthor> Create(IChatAuthorsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId) = command;
        if (Computed.IsInvalidating()) {
            if (!userId.IsNullOrEmpty()) {
                _ = GetByUserId(chatId, userId, true, default);
                _ = GetByUserId(chatId, userId, false, default);
                _ = GetChatIdsByUserId(userId, default);
                _ = GetUserIds(chatId, default);
            }
            _ = GetAuthorIds(chatId, default);
            return default!;
        }

        DbChatAuthor? dbChatAuthor;
        if (userId.IsNullOrEmpty()) {
            var name = RandomNameGenerator.Generate('_');
            dbChatAuthor = new DbChatAuthor() {
                Name = name,
                IsAnonymous = true,
            };
        }
        else {
            var userAuthor = await UserProfilesBackend.GetUserAuthor(userId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException();
            dbChatAuthor = new DbChatAuthor() {
                IsAnonymous = userAuthor.IsAnonymous,
            };
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        dbChatAuthor.ChatId = chatId;
        dbChatAuthor.LocalId = await DbNextLocalId(dbContext, chatId, cancellationToken).ConfigureAwait(false);
        dbChatAuthor.Id = DbChatAuthor.ComposeId(chatId, dbChatAuthor.LocalId);
        dbChatAuthor.UserId = userId.NullIfEmpty();
        dbContext.Add(dbChatAuthor);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        CommandContext.GetCurrent().Items.Set(chatAuthor);
        return chatAuthor;
    }

    /// <summary> The filter which creates default avatar for anonymous chat author</summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnChatAuthorCreated(
        IChatAuthorsBackend.CreateCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        if (Computed.IsInvalidating())
            return;

        var chatAuthor = context.Items.Get<ChatAuthor>()!;
        if (!chatAuthor.UserId.IsEmpty)
            return;
        await UserAvatarsBackend.EnsureChatAuthorAvatarCreated(chatAuthor.Id, chatAuthor.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private / internal methods

    private async Task<long> DbNextLocalId(
        ChatDbContext dbContext,
        string chatId,
        CancellationToken cancellationToken)
    {
        var idSequenceKey = new Symbol(chatId);
        var maxLocalId = _maxLocalIdCache.GetValueOrDefault(idSequenceKey);
        if (maxLocalId == 0) {
            _maxLocalIdCache[idSequenceKey] = maxLocalId =
                await dbContext.ChatAuthors.ForUpdate() // To serialize inserts
                    .Where(e => e.ChatId == chatId)
                    .OrderByDescending(e => e.LocalId)
                    .Select(e => e.LocalId)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        var localId = await IdSequences.Next(idSequenceKey, maxLocalId).ConfigureAwait(false);
        _maxLocalIdCache[idSequenceKey] = localId;
        return localId;
    }

    private async Task<ChatAuthor?> InheritFromUserAuthor(ChatAuthor? chatAuthor, bool inherit, CancellationToken cancellationToken)
    {
        if (!inherit || chatAuthor == null)
            return chatAuthor;

        if (!chatAuthor.UserId.IsEmpty) {
            var chatUserSettings = await ChatUserSettingsBackend
                .Get(chatAuthor.UserId.Value, chatAuthor.ChatId, cancellationToken)
                .ConfigureAwait(false);
            var avatarId = chatUserSettings?.AvatarId ?? Symbol.Empty;
            if (!avatarId.IsEmpty) {
                var avatar = await UserAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
                return chatAuthor.InheritFrom(avatar);
            }

            var userAuthor = await UserProfilesBackend.GetUserAuthor(chatAuthor.UserId, cancellationToken)
                .ConfigureAwait(false);
            return chatAuthor.InheritFrom(userAuthor);
        }
        else {
            var avatarId = await UserAvatarsBackend.GetAvatarIdByChatAuthorId(chatAuthor.Id, cancellationToken)
                .ConfigureAwait(false);
            var avatar = await UserAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            return chatAuthor.InheritFrom(avatar);
        }
    }
}
