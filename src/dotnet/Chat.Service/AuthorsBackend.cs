using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Commands;
using ActualChat.Db;
using ActualChat.Users;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class AuthorsBackend : DbServiceBase<ChatDbContext>, IAuthorsBackend
{
    private IChatsBackend? _chatsBackend;

    private IAccountsBackend AccountsBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbAuthor> DbAuthorResolver { get; }
    private IDbShardLocalIdGenerator<DbAuthor, string> DbAuthorLocalIdGenerator { get; }
    private DiffEngine DiffEngine { get; }

    public AuthorsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        DbAuthorResolver = services.GetRequiredService<IDbEntityResolver<string, DbAuthor>>();
        DbAuthorLocalIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbAuthor, string>>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> Get(
        ChatId chatId, AuthorId authorId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNone || authorId.IsNone || authorId.ChatId != chatId)
            return null;

        if (authorId == Bots.GetWalleId(chatId))
            return Bots.GetWalle(chatId);

        var dbAuthor = await DbAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
        AuthorFull? author;
        if (dbAuthor == null) {
            if (!chatId.IsPeerChatId(out var peerChatId))
                return null;

            author = GetDefaultPeerChatAuthor(peerChatId, authorId);
            if (author == null)
                return null;
        }
        else
            author = dbAuthor.ToModel();

        author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetByUserId(
        ChatId chatId, UserId userId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNone || userId.IsNone)
            return null;

        if (userId == Constants.User.Walle.UserId)
            return Bots.GetWalle(chatId);

        AuthorFull? author;
        { // Closes "using" block earlier
            var dbContext = CreateDbContext();
            await using var _ = dbContext.ConfigureAwait(false);

            var dbAuthor = await dbContext.Authors
                .Include(a => a.Roles)
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            author = dbAuthor?.ToModel();
        }
        if (author == null) {
            if (!chatId.IsPeerChatId(out var peerChatId))
                return null;

            author = GetDefaultPeerChatAuthor(peerChatId, userId);
            if (author == null)
                return null;
        }

        author = await AddAvatar(author, cancellationToken).ConfigureAwait(false);
        return author;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<AuthorId>> ListAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return ImmutableArray<AuthorId>.Empty;

        if (chatId.IsPeerChatId(out var peerChatId))
            return GetDefaultPeerChatAuthors(peerChatId).Select(a => a.Id).ToImmutableArray();

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
        if (chatId.IsNone)
            return ImmutableArray<UserId>.Empty;

        if (chatId.IsPeerChatId(out var peerChatId))
            return GetDefaultPeerChatAuthors(peerChatId).Select(a => a.UserId).ToImmutableArray();

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
    public virtual async Task<AuthorFull> Upsert(IAuthorsBackend.UpsertCommand command, CancellationToken cancellationToken)
    {
        var (chatId, authorId, userId, expectedVersion, diff) = command;
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(command), "Invalid ChatId.");
        if (!authorId.IsNone && authorId.ChatId != chatId)
            throw new ArgumentOutOfRangeException(nameof(command), "Invalid AuthorId.");
        if (userId.IsNone && authorId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(command), "Either AuthorId or UserId must be provided.");

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var (invAuthor, invOldAuthor) = context.Operation().Items.GetOrDefault<(AuthorFull, AuthorFull?)>();
            if (!invAuthor.Id.IsNone) {
                _ = Get(chatId, invAuthor.Id, default);
                _ = GetByUserId(chatId, invAuthor.UserId, default);
                var invOldHadLeft = invOldAuthor?.HasLeft ?? true;
                if (invAuthor.HasLeft != invOldHadLeft) {
                    _ = ListAuthorIds(chatId, default);
                    _ = ListUserIds(chatId, default);
                }
            }
            return default!;
        }

        var defaultAuthor = chatId.IsPeerChatId(out var peerChatId)
            ? GetDefaultPeerChatAuthor(peerChatId, authorId, userId).Require()
            : null;

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAuthors = dbContext.Authors.ForUpdate().Include(a => a.Roles);
        var dbAuthor = await (authorId.IsNone
            ? dbAuthors.SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
            : dbAuthors.SingleOrDefaultAsync(a => a.ChatId == chatId && a.Id == authorId, cancellationToken)
            ).ConfigureAwait(false);
        var existingAuthor = dbAuthor?.ToModel() ?? defaultAuthor;

        if (existingAuthor != null) {
            // Update existing author, incl. one of the default ones in peer chat
            existingAuthor.RequireVersion(expectedVersion);
            var account = await AccountsBackend.Get(existingAuthor.UserId, cancellationToken).Require().ConfigureAwait(false);

            var author = DiffEngine.Patch(existingAuthor, diff) with {
                Version = VersionGenerator.NextVersion(existingAuthor.Version),
            };

            // Check constraints
            if (author.IsAnonymous) {
                if (!existingAuthor.IsAnonymous)
                    throw StandardError.Constraint("IsAnonymous can be changed only to false.");
            }
            else if (account.IsGuest)
                throw StandardError.Constraint("Unauthenticated authors must be anonymous.");
            if (author.HasLeft && !peerChatId.IsNone)
                throw StandardError.Constraint("Peer chat authors can't leave.");

            if (dbAuthor == null) {
                // First author update in peer chat = create it
                dbAuthor = new DbAuthor(author);
                dbContext.Add(dbAuthor);
            }
            else
                dbAuthor.UpdateFrom(author);
        }
        else {
            // Create author, + we know here it's not a peer chat
            if (userId.IsNone)
                throw new ArgumentOutOfRangeException(nameof(command), "UserId is required to create a new author.");
            var account = await AccountsBackend.Get(userId, cancellationToken).Require().ConfigureAwait(false);

            var localId = await DbAuthorLocalIdGenerator
                .Next(dbContext, chatId, cancellationToken)
                .ConfigureAwait(false);
            var id = new AuthorId(chatId, localId, AssumeValid.Option);
            var author = new AuthorFull(id, VersionGenerator.NextVersion()) {
                UserId = userId,
                IsAnonymous = account.IsGuest,
            };
            author = DiffEngine.Patch(author, diff);

            // Check constraints
            if (author.HasLeft)
                throw StandardError.Constraint("New authors can't instantly leave the chat.");
            if (author.IsAnonymous) {
                if (author.AvatarId.IsEmpty) {
                    // Creating a random avatar for anonymous authors w/o pre-selected avatar
                    var changeCommand = new IAvatarsBackend.ChangeCommand(Symbol.Empty, null, new Change<AvatarFull>() {
                        Create = new AvatarFull() {
                            UserId = userId,
                            Name = RandomNameGenerator.Default.Generate(),
                            Bio = "Someone anonymous",
                            Picture = "", // NOTE(AY): Add a random one?
                        },
                    });
                    var avatar = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
                    author = author with { AvatarId = avatar.Id };
                }
            }
            else if (account.IsGuest)
                throw StandardError.Constraint("Unauthenticated authors must be anonymous.");

            dbAuthor = new DbAuthor(author);
            dbContext.Add(dbAuthor);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        { // Nested to get a new var scope
            var author = dbAuthor.ToModel();
            context.Operation().Items.Set((author, existingAuthor));

            if (peerChatId.IsNone) {
                // Set chat read position to the very end
                var chatTextIdRange = await ChatsBackend
                    .GetIdRange(command.ChatId, ChatEntryKind.Text, false, cancellationToken)
                    .ConfigureAwait(false);
                new IReadPositionsBackend.SetCommand(author.UserId, command.ChatId, chatTextIdRange.End - 1)
                    .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId));
            }
            // Raise AuthorChangedEvent
            new AuthorChangedEvent(author, existingAuthor)
                .EnqueueOnCompletion(Queues.Users.ShardBy(author.UserId), Queues.Chats.ShardBy(chatId));
            return author;
        }
    }

    [EventHandler]
    public virtual async Task OnAvatarChangedEvent(AvatarChangedEvent @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (_, oldAvatar, changeKind) = @event;
        if (changeKind != ChangeKind.Remove)
            return;

        oldAvatar = oldAvatar.Require();

        var authors = await ListAuthorsByAvatarId(oldAvatar.UserId, oldAvatar.Id, cancellationToken).ConfigureAwait(false);

        foreach (var author in authors) {
            var command = new IAuthorsBackend.UpsertCommand(author.ChatId,
                author.Id,
                author.UserId,
                author.Version,
                new AuthorDiff { AvatarId = null, });
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ImmutableList<AuthorFull>> ListAuthorsByAvatarId(UserId userId, Symbol avatarId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbAuthors = await dbContext.Authors
            .Where(a => a.UserId == userId && a.AvatarId == avatarId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAuthors.Select(x => x.ToModel()).ToImmutableList();
    }

    // Private / internal methods

    private async ValueTask<AuthorFull> AddAvatar(AuthorFull author, CancellationToken cancellationToken)
    {
        var avatarId = author.AvatarId;
        if (!avatarId.IsEmpty) {
            var avatar = await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar != null)
                return author with { Avatar = avatar };
        }
        var userId = author.UserId;
        var account = userId.IsNone ? null
            : await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        return author with { Avatar = account?.Avatar ?? GetDefaultAvatar(author) };
    }

    private AvatarFull GetDefaultAvatar(AuthorFull author)
        => new() {
            Name = RandomNameGenerator.Default.Generate(author.Id),
            Picture = DefaultUserPicture.GetAvataaar(author.Id),
            Bio = "",
        };

    private AuthorFull[] GetDefaultPeerChatAuthors(PeerChatId chatId)
        => new[] {
            GetDefaultPeerChatAuthor(chatId, chatId.UserId1)!,
            GetDefaultPeerChatAuthor(chatId, chatId.UserId2)!,
        };

    private AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, AuthorId authorId, UserId userId)
        => authorId.IsNone
            ? GetDefaultPeerChatAuthor(chatId, userId)
            : GetDefaultPeerChatAuthor(chatId, authorId);

    private AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, AuthorId authorId)
    {
        if (authorId.ChatId.Id != chatId.Id)
            return null;
        if (authorId.LocalId == 1)
            return GetDefaultPeerChatAuthor(chatId, chatId.UserId1);
        if (authorId.LocalId == 2)
            return GetDefaultPeerChatAuthor(chatId, chatId.UserId2);
        return null;
    }

    private AuthorFull? GetDefaultPeerChatAuthor(PeerChatId chatId, UserId userId)
    {
        var localId = chatId.IndexOf(userId) + 1;
        if (localId < 1)
            return null;

        var authorId = new AuthorId(chatId, localId, AssumeValid.Option);
        var author = new AuthorFull(authorId) {
            IsAnonymous = false,
            UserId = userId,
            AvatarId = "",
            HasLeft = false,
        };
        return author;
    }
}
