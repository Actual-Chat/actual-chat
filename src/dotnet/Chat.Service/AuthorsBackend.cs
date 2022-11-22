using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Commands;
using ActualChat.Db;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class AuthorsBackend : DbServiceBase<ChatDbContext>, IAuthorsBackend
{
    private IChatsBackend? _chatsBackend;
    private IAuthors? _frontend;

    private IAccounts Accounts { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbAuthor> DbAuthorResolver { get; }
    private IDbShardLocalIdGenerator<DbAuthor, string> DbAuthorLocalIdGenerator { get; }
    private IServerKvas ServerKvas { get; }
    private IAuthors Frontend => _frontend ??= Services.GetRequiredService<IAuthors>();

    public AuthorsBackend(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        DbAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbAuthor>>();
        DbAuthorLocalIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbAuthor, string>>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        ServerKvas = services.ServerKvas();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> Get(
        ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        if (!AuthorId.TryParse(authorId, out var parsedAuthorId))
            return null;
        if (!ChatId.TryParse(chatId, out var parsedChatId))
            return null;
        if (parsedAuthorId.ChatId != parsedChatId)
            return null;

        if (parsedAuthorId == AuthorExt.GetWalleId(parsedChatId))
            return AuthorExt.GetWalle(parsedChatId);

        var dbAuthor = await DbAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        if (dbAuthor == null || !OrdinalEquals(dbAuthor.ChatId, chatId.Value))
            return null;

        var author = dbAuthor.ToModel();
        author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetByUserId(
        ChatId chatId, UserId userId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty || userId.IsEmpty)
            return null;

        if (userId == Constants.User.Walle.UserId)
            return AuthorExt.GetWalle(chatId);

        AuthorFull? author;
        { // Closes "using" block earlier
            var dbContext = CreateDbContext();
            await using var _ = dbContext.ConfigureAwait(false);

            var dbAuthor = await dbContext.Authors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            author = dbAuthor?.ToModel();
            if (author == null)
                return null;
        }

        author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // Not a [ComputeMethod]!
    public async Task<AuthorFull> GetOrCreate(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var author = await Frontend.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var userId = account?.Id ?? default;

        var command = new IAuthorsBackend.CreateCommand(chatId, userId, false);
        author = await Commander.Call(command, false, cancellationToken).ConfigureAwait(false);

        if (account == null) {
            var kvas = ServerKvas.GetClient(session);
            var settings = await kvas.GetUnregisteredUserSettings(cancellationToken).ConfigureAwait(false);
            settings = settings.WithChat(chatId, author.Id);
            await kvas.SetUnregisteredUserSettings(settings, cancellationToken).ConfigureAwait(false);
        }
        return author;
    }

    // Not a [ComputeMethod]!
    public async Task<AuthorFull> GetOrCreate(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        var author = await GetByUserId(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (author != null)
            return author;

        var command = new IAuthorsBackend.CreateCommand(chatId, userId, true);
        author = await Commander.Call(command, false, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<AuthorId>> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return ImmutableArray<AuthorId>.Empty;

        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var authorIds = await dbContext.Authors
            .Where(a => a.ChatId == chatId && !a.HasLeft)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Select(x => new AuthorId(x)).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<UserId>> ListUserIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return ImmutableArray<UserId>.Empty;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.Authors
            .Where(a => a.ChatId == chatId && !a.HasLeft && a.UserId != null)
            .Select(a => a.UserId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(x => new UserId(x)).ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> Create(IAuthorsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId, requireAccount) = command;
        if (Computed.IsInvalidating()) {
            if (!userId.IsEmpty) {
                _ = GetByUserId(chatId, userId, default);
                _ = ListUserIds(chatId, default);
            }
            _ = ListAuthorIds(chatId, default);
            return default!;
        }

        AvatarFull? newAvatar = null;
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbAuthor? dbAuthor;
        if (account == null) {
            if (requireAccount)
                throw StandardError.Constraint("Can't create unauthenticated author here.");

            // We're creating an author for unregistered user here,
            // so we have to create a new random avatar
            var changeCommand = new IAvatarsBackend.ChangeCommand(Symbol.Empty, null, new Change<AvatarFull>() {
                Create = new AvatarFull() {
                    Id = Symbol.Empty,
                    Version = VersionGenerator.NextVersion(),
                    Name = RandomNameGenerator.Default.Generate(),
                    Bio = "Unregistered user",
                    Picture = "", // NOTE(AY): Add a random one?
                },
            });
            newAvatar = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        }

        if (newAvatar != null) {
            // We're creating an author for unregistered user
            dbAuthor = new() {
                AvatarId = newAvatar.Id,
                IsAnonymous = true,
            };
        }
        else {
            // We're creating an author for registered user,
            // so it makes sense to check if such an author already exists
            dbAuthor = await dbContext.Authors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId.Value && a.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbAuthor != null)
                return dbAuthor.ToModel(); // Already exist, so we don't recreate one

            dbAuthor = new DbAuthor() {
                IsAnonymous = false,
            };
        }

        dbAuthor.ChatId = chatId;
        dbAuthor.LocalId = await DbAuthorLocalIdGenerator
            .Next(dbContext, chatId, cancellationToken)
            .ConfigureAwait(false);
        dbAuthor.Id = DbAuthor.ComposeId(chatId, dbAuthor.LocalId);
        dbAuthor.Version = VersionGenerator.NextVersion();
        dbAuthor.UserId = account?.Id;
        dbContext.Add(dbAuthor);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var author = dbAuthor.ToModel();
        CommandContext.GetCurrent().Items.Set(author);

        if (newAvatar != null) {
            // We're creating an author for unregistered user here,
            // and its newAvatar.PrincipalId is empty here,
            // so we need to set it to the right one as a follow-up.
            newAvatar = newAvatar with {
                PrincipalId = author.Id,
            };
            new IAvatarsBackend.ChangeCommand(newAvatar.Id, newAvatar.Version, new Change<AvatarFull>() {
                Update = newAvatar,
            }).EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId));
        }

        var chatTextIdRange = await ChatsBackend
            .GetIdRange(command.ChatId, ChatEntryKind.Text, false, cancellationToken)
            .ConfigureAwait(false);
        new AuthorChangedEvent(author, ChangeKind.Create)
            .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId), Queues.Chats.ShardBy(chatId));
        new IReadPositionsBackend.SetCommand(command.UserId, command.ChatId, chatTextIdRange.End - 1)
            .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId));
        return author;
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> ChangeHasLeft(IAuthorsBackend.ChangeHasLeftCommand command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, hasLeft) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invAuthor = context.Operation().Items.Get<AuthorFull>();
            if (invAuthor == null)
                return default!; // No change was made

            var userId = invAuthor.UserId;
            if (!userId.IsEmpty) {
                _ = GetByUserId(chatId, userId, default);
                _ = ListUserIds(chatId, default);
            }
            _ = Get(invAuthor.ChatId, invAuthor.Id, default);
            _ = ListAuthorIds(chatId, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAuthor = await dbContext.Authors
            .ForUpdate()
            .Include(a => a.Roles)
            .SingleAsync(a => a.Id == authorId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbAuthor.HasLeft == hasLeft)
            return dbAuthor.ToModel();

        dbAuthor.HasLeft = hasLeft;
        dbAuthor.Version = VersionGenerator.NextVersion(dbAuthor.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var author = dbAuthor.ToModel();
        context.Operation().Items.Set(author);
        new AuthorChangedEvent(author, ChangeKind.Update)
            .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId), Queues.Chats.ShardBy(chatId));
        return author;
    }

    // [CommandHandler]
    public virtual async Task<AuthorFull> SetAvatar(IAuthorsBackend.SetAvatarCommand command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, avatarId) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invAuthor = context.Operation().Items.Get<AuthorFull>()!;
            var userId = invAuthor.UserId;
            if (!userId.IsEmpty) {
                _ = GetByUserId(chatId, userId, default);
                _ = ListUserIds(chatId, default);
            }
            _ = Get(invAuthor.ChatId, invAuthor.Id, default);
            _ = ListAuthorIds(chatId, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAuthor = await dbContext.Authors
            .Include(a => a.Roles)
            .SingleAsync(a => a.Id == authorId.Value, cancellationToken)
            .ConfigureAwait(false);
        dbAuthor.AvatarId = avatarId;
        dbAuthor.Version = VersionGenerator.NextVersion(dbAuthor.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var author = dbAuthor.ToModel();
        context.Operation().Items.Set(author);
        return author;
    }

    // Private / internal methods

    private async ValueTask<AuthorFull> AddAvatar(AuthorFull author, CancellationToken cancellationToken)
    {
        var avatarId = author.AvatarId;
        if (!avatarId.IsEmpty) {
            var avatar = await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar != null)
                return WithAvatar(author, avatar);
        }
        var userId = author.UserId;
        if (!userId.IsEmpty) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account != null)
                return WithAvatar(author, account.Avatar);
        }
        return WithAvatar(author, GetDefaultAvatar(author));

        AuthorFull WithAvatar(AuthorFull a, Avatar avatar)
            => a with { Avatar = avatar };
    }

    private AvatarFull GetDefaultAvatar(AuthorFull author)
        => new() {
            Id = default,
            Name = RandomNameGenerator.Default.Generate(author.Id),
            Picture = DefaultUserPicture.GetAvataaar(author.Id),
            Bio = "",
        };
}
