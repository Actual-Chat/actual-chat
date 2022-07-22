using System.ComponentModel.DataAnnotations;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public class ChatAuthorsBackend : DbServiceBase<ChatDbContext>, IChatAuthorsBackend
{
    private const string AuthorIdSuffix = "::authorId";
    private IChatAuthors? _frontend;

    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IUserAvatarsBackend UserAvatarsBackend { get; }
    private RedisSequenceSet<ChatAuthor> IdSequences { get; }
    private IRandomNameGenerator RandomNameGenerator { get; }
    private IDbEntityResolver<string, DbChatAuthor> DbChatAuthorResolver { get; }
    private IDbShardLocalIdGenerator<DbChatAuthor, string> DbChatAuthorLocalIdGenerator { get; }
    private IChatUserSettingsBackend ChatUserSettingsBackend { get; }
    private IChatAuthors Frontend => _frontend ??= Services.GetRequiredService<IChatAuthors>();

    public ChatAuthorsBackend(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        IdSequences = services.GetRequiredService<RedisSequenceSet<ChatAuthor>>();
        RandomNameGenerator = services.GetRequiredService<IRandomNameGenerator>();
        DbChatAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatAuthor>>();
        DbChatAuthorLocalIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbChatAuthor, string>>();
        UserAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        ChatUserSettingsBackend = services.GetRequiredService<IChatUserSettingsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> Get(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var dbChatAuthor = await DbChatAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        if (!OrdinalEquals(dbChatAuthor?.ChatId, chatId))
            return null;
        var chatAuthor = dbChatAuthor!.ToModel();
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
        { // Closes "using" block earlier
            var dbContext = CreateDbContext();
            await using var _ = dbContext.ConfigureAwait(false);

            var dbChatAuthor = await dbContext.ChatAuthors
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            chatAuthor = dbChatAuthor?.ToModel();
        }

        return await InheritFromUserAuthor(chatAuthor, inherit, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListUserChatIds(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return ImmutableArray<Symbol>.Empty;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var chatIds = await dbContext.ChatAuthors
            .Where(a => a.UserId == userId && !a.HasLeft)
            .Select(a => a.ChatId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return chatIds.Select(x => new Symbol(x)).ToImmutableArray();
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chatAuthor = await Frontend.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        var userId = account?.Id ?? Symbol.Empty;

        var cmd = new IChatAuthorsBackend.CreateCommand(chatId, userId, false);
        chatAuthor = await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);

        if (account == null) {
            var updateOptionCommand = new ISessionOptionsBackend.UpsertCommand(
                session,
                new(chatId + AuthorIdSuffix, chatAuthor.Id));
            await Commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return chatAuthor;
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(string chatId, string userId, bool inherit, CancellationToken cancellationToken)
    {
        var chatAuthor = await GetByUserId(chatId, userId, inherit, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var cmd = new IChatAuthorsBackend.CreateCommand(chatId, userId);
        chatAuthor = await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        return chatAuthor;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty())
            return ImmutableArray<Symbol>.Empty;

        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var authorIds = await dbContext.ChatAuthors
            .Where(a => a.ChatId == chatId && !a.HasLeft)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Select(x => new Symbol(x)).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListUserIds(string chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty())
            return ImmutableArray<Symbol>.Empty;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.ChatAuthors
            .Where(a => a.ChatId == chatId && !a.HasLeft && a.UserId != null)
            .Select(a => a.UserId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(x => new Symbol(x)).ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task<ChatAuthor> Create(IChatAuthorsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId, requireAuthenticated) = command;
        if (Computed.IsInvalidating()) {
            if (!userId.IsNullOrEmpty()) {
                _ = GetByUserId(chatId, userId, true, default);
                _ = GetByUserId(chatId, userId, false, default);
                _ = ListUserChatIds(userId, default);
                _ = ListUserIds(chatId, default);
            }
            _ = ListAuthorIds(chatId, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbChatAuthor? dbChatAuthor;
        if (userId.IsNullOrEmpty()) {
            if (requireAuthenticated)
                throw new ValidationException("Can't create unauthenticated author here.");

            var name = RandomNameGenerator.Generate('_');
            dbChatAuthor = new DbChatAuthor() {
                Name = name,
                IsAnonymous = true,
            };
        }
        else {
            dbChatAuthor = await dbContext.ChatAuthors
                .FirstOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            if (dbChatAuthor != null)
                return dbChatAuthor.ToModel(); // Author already exists

            var userAuthor = await AccountsBackend.GetUserAuthor(userId, cancellationToken).Require().ConfigureAwait(false);
            dbChatAuthor = new DbChatAuthor() {
                IsAnonymous = userAuthor.IsAnonymous,
            };
        }

        dbChatAuthor.ChatId = chatId;
        dbChatAuthor.LocalId = await DbChatAuthorLocalIdGenerator
            .Next(dbContext, chatId, cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.Id = DbChatAuthor.ComposeId(chatId, dbChatAuthor.LocalId);
        dbChatAuthor.UserId = userId.NullIfEmpty();
        dbContext.Add(dbChatAuthor);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        CommandContext.GetCurrent().Items.Set(chatAuthor);
        return chatAuthor;
    }

    public virtual async Task<ChatAuthor> ChangeHasLeft(IChatAuthorsBackend.ChangeHasLeftCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (authorId, hasLeft) = command;
        if (Computed.IsInvalidating()) {
            var invChatAuthor = context.Operation().Items.Get<ChatAuthor>()!;
            var userId = (string)invChatAuthor.UserId;
            var chatId = (string)invChatAuthor.ChatId;
            if (!userId.IsNullOrEmpty()) {
                _ = GetByUserId(chatId, userId, true, default);
                _ = GetByUserId(chatId, userId, false, default);
                _ = ListUserIds(chatId, default);
                _ = ListUserChatIds(userId, default);
            }
            _ = Get(invChatAuthor.ChatId, invChatAuthor.Id, false, default);
            _ = Get(invChatAuthor.ChatId, invChatAuthor.Id, true, default);
            _ = ListAuthorIds(chatId, default);

            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatAuthor = await dbContext.ChatAuthors
            .SingleAsync(a => a.Id == authorId, cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.HasLeft = hasLeft;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        context.Operation().Items.Set(chatAuthor);
        return chatAuthor;
    }

    [CommandHandler(IsFilter = true, Priority = 1001)] // 1000 = DbOperationScopeProvider, we must "wrap" it
    public virtual async Task OnChatAuthorCreated(
        IChatAuthorsBackend.CreateCommand command,
        CancellationToken cancellationToken)
    {
        // This filter creates default avatar for anonymous chat author

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        var chatAuthor = context.Items.Get<ChatAuthor>()!;
        if (!chatAuthor.UserId.IsEmpty)
            return;
        await UserAvatarsBackend.EnsureChatAuthorAvatarCreated(chatAuthor.Id, chatAuthor.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private / internal methods

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

            var userAuthor = await AccountsBackend.GetUserAuthor(chatAuthor.UserId, cancellationToken)
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
