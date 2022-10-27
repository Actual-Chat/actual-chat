using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class ChatAuthorsBackend : DbServiceBase<ChatDbContext>, IChatAuthorsBackend
{
    private IChatAuthors? _frontend;

    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IRandomNameGenerator RandomNameGenerator { get; }
    private IDbEntityResolver<string, DbChatAuthor> DbAuthorResolver { get; }
    private IDbShardLocalIdGenerator<DbChatAuthor, string> DbAuthorLocalIdGenerator { get; }
    private IServerKvas ServerKvas { get; }
    private IChatAuthors Frontend => _frontend ??= Services.GetRequiredService<IChatAuthors>();

    public ChatAuthorsBackend(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        RandomNameGenerator = services.GetRequiredService<IRandomNameGenerator>();
        DbAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatAuthor>>();
        DbAuthorLocalIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbChatAuthor, string>>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        ServerKvas = services.ServerKvas();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorFull?> Get(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var dbChatAuthor = await DbAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        if (!OrdinalEquals(dbChatAuthor?.ChatId, chatId))
            return null;
        var author = dbChatAuthor!.ToModel();
        return await InheritFromUserAuthor(author, inherit, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorFull?> GetByUserId(
        string chatId, string userId, bool inherit,
        CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty() || chatId.IsNullOrEmpty())
            return null;

        ChatAuthorFull? chatAuthor;
        { // Closes "using" block earlier
            var dbContext = CreateDbContext();
            await using var _ = dbContext.ConfigureAwait(false);

            var dbChatAuthor = await dbContext.ChatAuthors
                .Include(a => a.Roles)
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
            var kvas = ServerKvas.GetClient(session);
            var settings = await kvas.GetUnregisteredUserSettings(cancellationToken).ConfigureAwait(false);
            settings = settings.WithChat(chatId, chatAuthor.Id);
            await kvas.SetUnregisteredUserSettings(settings, cancellationToken).ConfigureAwait(false);
        }
        return chatAuthor;
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(string chatId, string userId, bool inherit, CancellationToken cancellationToken)
    {
        var chatAuthor = await GetByUserId(chatId, userId, inherit, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var cmd = new IChatAuthorsBackend.CreateCommand(chatId, userId, true);
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
    public virtual async Task<ChatAuthorFull> Create(IChatAuthorsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId, requireAccount) = command;
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

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbChatAuthor? dbChatAuthor;
        if (account == null) {
            if (requireAccount)
                throw StandardError.Constraint("Can't create unauthenticated author here.");

            dbChatAuthor = new() {
                Name = RandomNameGenerator.Generate(),
                Bio = "Unregistered user",
                Picture = "", // NOTE(AY): Add a random one?
                IsAnonymous = true,
            };
            dbContext.Add(dbChatAuthor);
        }
        else {
            // Let's check if the author already exists first
            dbChatAuthor = await dbContext.ChatAuthors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            if (dbChatAuthor != null)
                return dbChatAuthor.ToModel(); // Author already exist, so we don't recreate one

            dbChatAuthor = new DbChatAuthor() {
                IsAnonymous = false,
                AvatarId = account.Avatar.Id.Value.NullIfEmpty(),
            };
        }

        dbChatAuthor.ChatId = chatId;
        dbChatAuthor.LocalId = await DbAuthorLocalIdGenerator
            .Next(dbContext, chatId, cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.Id = DbChatAuthor.ComposeId(chatId, dbChatAuthor.LocalId);
        dbChatAuthor.UserId = account?.Id;


        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        CommandContext.GetCurrent().Items.Set(chatAuthor);
        return chatAuthor;
    }

    public virtual async Task<ChatAuthorFull> ChangeHasLeft(IChatAuthorsBackend.ChangeHasLeftCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (authorId, hasLeft) = command;
        if (Computed.IsInvalidating()) {
            var invChatAuthor = context.Operation().Items.Get<ChatAuthorFull>()!;
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
            .Include(a => a.Roles)
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

        var chatAuthor = context.Items.Get<ChatAuthorFull>();
        if (chatAuthor is not { UserId.IsEmpty: true })
            return;
        await AvatarsBackend.EnsureChatAuthorAvatarCreated(chatAuthor.Id, chatAuthor.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private / internal methods

    private async Task<ChatAuthorFull?> InheritFromUserAuthor(ChatAuthorFull? chatAuthor, bool inherit, CancellationToken cancellationToken)
    {
        if (!inherit || chatAuthor == null)
            return chatAuthor;

        if (!chatAuthor.UserId.IsEmpty) {
            var chatUserSettings = await ChatUserSettingsBackend
                .Get(chatAuthor.UserId.Value, chatAuthor.ChatId, cancellationToken)
                .ConfigureAwait(false);
            var avatarId = chatUserSettings?.AvatarId ?? Symbol.Empty;
            if (!avatarId.IsEmpty) {
                var avatar = await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
                return chatAuthor.InheritFrom(avatar);
            }

            var userAuthor = await AccountsBackend.GetUserAuthor(chatAuthor.UserId, cancellationToken)
                .ConfigureAwait(false);
            return chatAuthor.InheritFrom(userAuthor);
        }
        else {
            var avatarId = await AvatarsBackend.GetAvatarIdByChatAuthorId(chatAuthor.Id, cancellationToken)
                .ConfigureAwait(false);
            var avatar = await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            return chatAuthor.InheritFrom(avatar);
        }
    }
}
