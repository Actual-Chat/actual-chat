using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatAuthors
{
    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> Get(
        string chatId, string authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        ChatAuthor? chatAuthor;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChatAuthor = await dbContext.ChatAuthors
                .SingleOrDefaultAsync(a => a.Id == authorId, cancellationToken)
                .ConfigureAwait(false);
            chatAuthor = dbChatAuthor?.ToModel();
        }

        if (!inherit || chatAuthor == null || chatAuthor.UserId.IsEmpty)
            return chatAuthor;

        var userInfo = await _userInfos.Get(chatAuthor.UserId, cancellationToken).ConfigureAwait(false);
        return chatAuthor.InheritFrom(userInfo);
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
        if (!inherit || chatAuthor == null || chatAuthor.UserId.IsEmpty)
            return chatAuthor;

        var userInfo = await _userInfos.Get(userId, cancellationToken).ConfigureAwait(false);
        return chatAuthor.InheritFrom(userInfo);
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(Session session, string chatId, CancellationToken cancellationToken)
    {
        var chatAuthor = await GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
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
                Name = userAuthor.Name, // Wrong: it should either generate anon
                Picture = userAuthor.Picture,
                IsAnonymous = userAuthor.IsAnonymous,
            };
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var maxLocalId = await dbContext.ChatAuthors.ForUpdate() // To serialize inserts
            .Where(e => e.ChatId == chatId)
            .OrderByDescending(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.ChatId = chatId;
        dbChatAuthor.LocalId = await _idSequences.Next(chatId, maxLocalId).ConfigureAwait(false);
        dbChatAuthor.Id = DbChatAuthor.ComposeId(chatId, dbChatAuthor.LocalId);
        dbChatAuthor.UserId = userId.NullIfEmpty();
        dbContext.Add(dbChatAuthor);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatAuthor.ToModel();
    }
}
