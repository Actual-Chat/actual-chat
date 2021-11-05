using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatAuthorsService
{
    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> Get(
        ChatId chatId, AuthorId authorId, bool inherit,
        CancellationToken cancellationToken)
    {
        ChatAuthor? chatAuthor;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChatAuthor = await dbContext.ChatAuthors
                .SingleOrDefaultAsync(a => a.Id == (string)authorId, cancellationToken)
                .ConfigureAwait(false);
            chatAuthor = dbChatAuthor?.ToModel();
        }

        if (!inherit || chatAuthor == null || chatAuthor.UserId.IsNone)
            return chatAuthor;

        var userInfo = await _userInfos.Get(chatAuthor.UserId, cancellationToken).ConfigureAwait(false);
        return chatAuthor.InheritFrom(userInfo);
    }

    // [ComputeMethod]
    public virtual async Task<ChatAuthor?> GetByUserId(
        ChatId chatId, UserId userId, bool inherit,
        CancellationToken cancellationToken)
    {
        if (userId.IsNone || chatId.IsNone)
            return null;

        ChatAuthor? chatAuthor;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChatAuthor = await dbContext.ChatAuthors
                .SingleOrDefaultAsync(a => a.ChatId == (string) chatId && a.UserId == (string)userId, cancellationToken)
                .ConfigureAwait(false);
            chatAuthor = dbChatAuthor?.ToModel();
        }
        if (!inherit || chatAuthor == null || chatAuthor.UserId.IsNone)
            return chatAuthor;

        var userInfo = await _userInfos.Get(userId, cancellationToken).ConfigureAwait(false);
        return chatAuthor.InheritFrom(userInfo);
    }

    // Not a [ComputeMethod]!
    public async Task<ChatAuthor> GetOrCreate(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chatAuthor = await GetSessionChatAuthor(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor != null)
            return chatAuthor;

        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        var userId = user.IsAuthenticated ? user.Id : UserId.None;

        var createAuthorCommand = new IChatAuthorsBackend.CreateAuthorCommand(chatId, userId);
        chatAuthor = await _commander.Call(createAuthorCommand, true, cancellationToken).ConfigureAwait(false);

        var updateOptionCommand = new ISessionOptionsBackend.UpdateCommand(
            session,
            new($"{chatId}::authorId", chatAuthor.Id)
            ).MarkValid();
        await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
        return chatAuthor;
    }

    // [CommandHandler]
    public virtual async Task<ChatAuthor> Create(IChatAuthorsBackend.CreateAuthorCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId) = command;
        if (Computed.IsInvalidating()) {
            _ = GetByUserId(chatId, userId, true, default);
            _ = GetByUserId(chatId, userId, false, default);
            return default!;
        }

        DbChatAuthor? dbChatAuthor;
        if (!userId.IsNone) {
            var userAuthor = await _userAuthorsBackend.Get(userId, true, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException();
            dbChatAuthor = new DbChatAuthor() {
                Name = userAuthor.Name, // Wrong: it should either generate anon
                Picture = userAuthor.Picture,
                IsAnonymous = userAuthor.IsAnonymous,
            };
        }
        else {
            var name = _randomNameGenerator.Generate('_', true);
            dbChatAuthor = new DbChatAuthor() {
                Name = name,
                Picture = "//eu.ui-avatars.com/api/?background=random&bold=true&length=1&name=" + name,
                IsAnonymous = true,
            };
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var maxLocalId = await dbContext.ChatAuthors.ForUpdate() // To serialize inserts
            .Where(e => e.ChatId == (string) chatId)
            .OrderByDescending(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthor.ChatId = chatId;
        dbChatAuthor.LocalId = await _idSequences.Next(chatId, maxLocalId).ConfigureAwait(false);
        dbChatAuthor.Id = DbChatAuthor.ComposeId(chatId, dbChatAuthor.LocalId);
        dbChatAuthor.UserId = userId.Value.NullIfEmpty();

        await dbContext.AddAsync(dbChatAuthor, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatAuthor.ToModel();
    }
}
