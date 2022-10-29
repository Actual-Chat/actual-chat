using ActualChat.Chat.Db;
using ActualChat.Commands;
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
    private IDbEntityResolver<string, DbAuthor> DbAuthorResolver { get; }
    private IDbShardLocalIdGenerator<DbAuthor, string> DbAuthorLocalIdGenerator { get; }
    private IServerKvas ServerKvas { get; }
    private IChatAuthors Frontend => _frontend ??= Services.GetRequiredService<IChatAuthors>();

    public ChatAuthorsBackend(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        RandomNameGenerator = services.GetRequiredService<IRandomNameGenerator>();
        DbAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbAuthor>>();
        DbAuthorLocalIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbAuthor, string>>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        ServerKvas = services.ServerKvas();
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorFull?> Get(
        string chatId, string authorId,
        CancellationToken cancellationToken)
    {
        var dbChatAuthor = await DbAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        if (dbChatAuthor == null || !OrdinalEquals(dbChatAuthor.ChatId, chatId))
            return null;

        var author = dbChatAuthor.ToModel();
        author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthorFull?> GetByUserId(
        string chatId, string userId,
        CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty() || chatId.IsNullOrEmpty())
            return null;

        ChatAuthorFull? author;
        { // Closes "using" block earlier
            var dbContext = CreateDbContext();
            await using var _ = dbContext.ConfigureAwait(false);

            var dbChatAuthor = await dbContext.ChatAuthors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            author = dbChatAuthor?.ToModel();
            if (author == null)
                return null;
        }

        author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
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
    public async Task<ChatAuthorFull> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken)
    {
        var author = await Frontend.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var userId = account?.Id ?? Symbol.Empty;

        var command = new IChatAuthorsBackend.CreateCommand(chatId, userId, false);
        author = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);

        if (account == null) {
            var kvas = ServerKvas.GetClient(session);
            var settings = await kvas.GetUnregisteredUserSettings(cancellationToken).ConfigureAwait(false);
            settings = settings.WithChat(chatId, author.Id);
            await kvas.SetUnregisteredUserSettings(settings, cancellationToken).ConfigureAwait(false);
        }
        return author;
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthorFull> GetOrCreate(string chatId, string userId, CancellationToken cancellationToken)
    {
        var chatAuthor = await GetByUserId(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var command = new IChatAuthorsBackend.CreateCommand(chatId, userId, true);
        chatAuthor = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
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
            if (!userId.IsEmpty) {
                _ = GetByUserId(chatId, userId, default);
                _ = GetByUserId(chatId, userId, default);
                _ = ListUserChatIds(userId, default);
                _ = ListUserIds(chatId, default);
            }
            _ = ListAuthorIds(chatId, default);
            return default!;
        }

        AvatarFull? newAvatar = null;
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbAuthor? dbChatAuthor;
        if (account == null) {
            if (requireAccount)
                throw StandardError.Constraint("Can't create unauthenticated author here.");

            // We're creating an author for unregistered user here,
            // so we have to create a new random avatar
            var changeCommand = new IAvatarsBackend.ChangeCommand(Symbol.Empty, null, new Change<AvatarFull>() {
                Create = new AvatarFull() {
                    Id = Symbol.Empty,
                    Version = VersionGenerator.NextVersion(),
                    Name = RandomNameGenerator.Generate(),
                    Bio = "Unregistered user",
                    Picture = "", // NOTE(AY): Add a random one?
                },
            });
            newAvatar = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        }

        if (newAvatar != null) {
            // We're creating an author for unregistered user
            dbChatAuthor = new() {
                AvatarId = newAvatar.Id,
                IsAnonymous = true,
            };
            dbContext.Add(dbChatAuthor);
        }
        else {
            // We're creating an author for registered user,
            // so it makes sense to check if such an author already exists
            dbChatAuthor = await dbContext.ChatAuthors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbChatAuthor != null)
                return dbChatAuthor.ToModel(); // Already exist, so we don't recreate one

            dbChatAuthor = new DbAuthor() {
                IsAnonymous = false,
            };
        }

        dbChatAuthor.ChatId = chatId;
        dbChatAuthor.LocalId = await DbAuthorLocalIdGenerator
            .Next(dbContext, chatId, cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.Id = DbAuthor.ComposeId(chatId, dbChatAuthor.LocalId);
        dbChatAuthor.Version = VersionGenerator.NextVersion();
        dbChatAuthor.UserId = account?.Id;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        CommandContext.GetCurrent().Items.Set(chatAuthor);

        if (newAvatar != null) {
            // We're creating an author for unregistered user here,
            // and its newAvatar.ChatPrincipalId is empty here,
            // so we need to set it to the right one as a follow-up.
            newAvatar = newAvatar with {
                ChatPrincipalId = chatAuthor.Id,
            };
            new IAvatarsBackend.ChangeCommand(newAvatar.Id, newAvatar.Version, new Change<AvatarFull>() {
                Update = newAvatar,
            }).EnqueueOnCompletion(Queues.Users);
        }
        return chatAuthor;
    }

    // [CommandHandler]
    public virtual async Task<ChatAuthorFull> ChangeHasLeft(IChatAuthorsBackend.ChangeHasLeftCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (chatId, authorId, hasLeft) = command;
        if (Computed.IsInvalidating()) {
            var invChatAuthor = context.Operation().Items.Get<ChatAuthorFull>()!;
            var userId = invChatAuthor.UserId;
            if (!userId.IsEmpty) {
                _ = GetByUserId(chatId, userId, default);
                _ = GetByUserId(chatId, userId, default);
                _ = ListUserIds(chatId, default);
                _ = ListUserChatIds(userId, default);
            }
            _ = Get(invChatAuthor.ChatId, invChatAuthor.Id, default);
            _ = Get(invChatAuthor.ChatId, invChatAuthor.Id, default);
            _ = ListAuthorIds(chatId, default);

            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatAuthor = await dbContext.ChatAuthors
            .Include(a => a.Roles)
            .SingleAsync(a => a.Id == authorId.Value, cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.HasLeft = hasLeft;
        dbChatAuthor.Version = VersionGenerator.NextVersion(dbChatAuthor.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        context.Operation().Items.Set(chatAuthor);
        return chatAuthor;
    }


    // [CommandHandler]
    public virtual async Task<ChatAuthorFull> SetAvatar(IChatAuthorsBackend.SetAvatarCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (chatId, authorId, avatarId) = command;
        if (Computed.IsInvalidating()) {
            var invChatAuthor = context.Operation().Items.Get<ChatAuthorFull>()!;
            var userId = invChatAuthor.UserId;
            if (!userId.IsEmpty) {
                _ = GetByUserId(chatId, userId, default);
                _ = GetByUserId(chatId, userId, default);
                _ = ListUserIds(chatId, default);
                _ = ListUserChatIds(userId, default);
            }
            _ = Get(invChatAuthor.ChatId, invChatAuthor.Id, default);
            _ = Get(invChatAuthor.ChatId, invChatAuthor.Id, default);
            _ = ListAuthorIds(chatId, default);

            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatAuthor = await dbContext.ChatAuthors
            .Include(a => a.Roles)
            .SingleAsync(a => a.Id == authorId.Value, cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.AvatarId = avatarId;
        dbChatAuthor.Version = VersionGenerator.NextVersion(dbChatAuthor.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var chatAuthor = dbChatAuthor.ToModel();
        context.Operation().Items.Set(chatAuthor);
        return chatAuthor;
    }

    // Private / internal methods

    private async ValueTask<ChatAuthorFull> AddAvatar(ChatAuthorFull author, CancellationToken cancellationToken)
    {
        if (author.AvatarId.IsEmpty) {
            var account = await AccountsBackend.Get(author.UserId, cancellationToken).Require().ConfigureAwait(false);
            author = author with {
                Avatar = account.Avatar,
            };
        }
        else {
            var avatar = await AvatarsBackend.Get(author.AvatarId, cancellationToken).Require().ConfigureAwait(false);
            author = author with {
                Avatar = avatar,
            };
        }
        return author;
    }
}
