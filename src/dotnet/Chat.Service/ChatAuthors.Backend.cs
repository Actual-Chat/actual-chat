using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatAuthors
{
    private readonly ThreadSafeLruCache<Symbol, long> _maxLocalIdCache = new(16384);

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> Get(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        var dbChatAuthor = await _dbChatAuthorResolver.Get(authorId, cancellationToken).ConfigureAwait(false);
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

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chatAuthor = await GetChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userId = user.IsAuthenticated ? user.Id : Symbol.Empty;

        var createAuthorCommand = new IChatAuthorsBackend.CreateCommand(chatId, userId);
        chatAuthor = await _commander.Call(createAuthorCommand, true, cancellationToken).ConfigureAwait(false);

        if (!user.IsAuthenticated) {
            var updateOptionCommand = new ISessionOptionsBackend.UpsertCommand(
                session,
                new($"{chatId}::authorId", chatAuthor.Id));
            await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return chatAuthor;
    }

    // [CommandHandler]
    public virtual async Task<ChatAuthor> Create(IChatAuthorsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId) = command;
        if (Computed.IsInvalidating()) {
            if (!userId.IsNullOrEmpty()) {
                _ = GetByUserId(chatId, userId, true, default);
                _ = GetByUserId(chatId, userId, false, default);
            }
            return default!;
        }

        DbChatAuthor? dbChatAuthor;
        if (userId.IsNullOrEmpty()) {
            var name = _randomNameGenerator.Generate('_');
            dbChatAuthor = new DbChatAuthor() {
                Name = name,
                Picture = "",
                IsAnonymous = true,
            };
        }
        else {
            var userAuthor = await _userAuthorsBackend.Get(userId, true, cancellationToken).ConfigureAwait(false)
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
        return dbChatAuthor.ToModel();
    }

    public virtual async Task Update(IChatAuthorsBackend.UpdateCommand command, CancellationToken cancellationToken)
    {
        var (authorId, name, picture) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var operation = CommandContext.GetCurrent().Operation();
            var invChatAuthor = operation.Items.Get<ChatAuthor>();
            if (invChatAuthor != null) {
                var invChatId = invChatAuthor.ChatId;
                var invUserId = invChatAuthor.UserId;
                if (!invUserId.IsEmpty) {
                    _ = GetByUserId(invChatId, invUserId, true, default);
                    _ = GetByUserId(invChatId, invUserId, false, default);
                }
                _ = Get(invChatId, authorId, true, default);
                _ = Get(invChatId, authorId, false, default);
            }
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatAuthor = await dbContext.ChatAuthors
            .SingleOrDefaultAsync(a => a.Id == authorId, cancellationToken)
            .ConfigureAwait(false);
        if (dbChatAuthor == null)
            throw new InvalidOperationException("chat author does not exists");

        dbChatAuthor.Name = name ?? "";
        dbChatAuthor.Picture = picture ?? "";
        dbChatAuthor.Version = VersionGenerator.NextVersion();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var сhatAuthor = dbChatAuthor.ToModel();
        context.Operation().Items.Set(сhatAuthor);
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

        var localId = await _idSequences.Next(idSequenceKey, maxLocalId).ConfigureAwait(false);
        _maxLocalIdCache[idSequenceKey] = localId;
        return localId;
    }

    private async Task<ChatAuthor?> InheritFromUserAuthor(ChatAuthor? chatAuthor, bool inherit, CancellationToken cancellationToken)
    {
        if (!inherit || chatAuthor == null || chatAuthor.UserId.IsEmpty)
            return chatAuthor;

        var userAuthor = await _userAuthorsBackend.Get(chatAuthor.UserId, true, cancellationToken).ConfigureAwait(false);
        if (userAuthor == null)
            return chatAuthor;

        return chatAuthor.InheritFrom(userAuthor);
    }
}
