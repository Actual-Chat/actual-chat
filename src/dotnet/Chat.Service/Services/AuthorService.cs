using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <inheritdoc cref="IAuthorService"/>
internal class AuthorService : DbServiceBase<ChatDbContext>, IAuthorService
{
    private readonly IDefaultAuthorService _defaultAuthorService;
    private readonly INicknameGenerator _nicknameGenerator;
    private readonly IUserStateService _userStateService;
    private readonly ILogger<AuthorService> _log;

    public AuthorService(
        IDefaultAuthorService defaultAuthorService,
        INicknameGenerator nicknameGenerator,
        IUserStateService userStateService,
        ILogger<AuthorService> log,
        IServiceProvider serviceProvider
    ) : base(serviceProvider)
    {
        _defaultAuthorService = defaultAuthorService;
        _nicknameGenerator = nicknameGenerator;
        _userStateService = userStateService;
        _log = log;
    }

    /// <inheritdoc />
    public virtual async Task<Author?> GetByAuthorId(AuthorId authorId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext(readWrite: false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUser = await dbContext.ChatUsers
            .Include(x => x.Author)
            .FirstOrDefaultAsync(x => x.AuthorId == (string)authorId, cancellationToken)
            .ConfigureAwait(false);

        if (dbUser?.Author == null)
            return null;

        var isOnline = false;
        IAuthorInfo? defaultAuthor = null;
        if (dbUser.UserId == null)
            return new(dbUser.Author) { UserId = UserId.None, AuthorId = authorId };

        UserId userId = dbUser.UserId;

        defaultAuthor = await _defaultAuthorService.Get(userId, cancellationToken)
           .ConfigureAwait(false);
        isOnline = await _userStateService.IsOnline(userId, cancellationToken).ConfigureAwait(false);

        if (defaultAuthor == null) {
            _log.LogError("The default author service returned <null>. " +
                "The userId: {UserId} should have a default author.", userId);
            return new(dbUser.Author) { UserId = userId, AuthorId = authorId };
        }
        return ToModel(dbUser, defaultAuthor, isOnline);
    }

    /// <inheritdoc />
    public virtual async Task<Author?> GetByUserIdAndChatId(
        UserId userId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (userId.IsNone || chatId.IsNone)
            return null;

        var dbContext = CreateDbContext(readWrite: false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUser = await dbContext.ChatUsers
            .Include(x => x.Author)
            .FirstOrDefaultAsync(x => x.UserId == (string)userId && x.ChatId == (string)chatId, cancellationToken)
            .ConfigureAwait(false);

        if (dbUser?.Author == null)
            return null;

        var defaultAuthor = await _defaultAuthorService.Get(userId, cancellationToken).ConfigureAwait(false);
        if (defaultAuthor == null) {
            _log.LogError("The default author service returned <null>. " +
                "The userId: {UserId} should have a default author.", userId);
            return null;
        }

        var isOnline = await _userStateService.IsOnline(userId, cancellationToken).ConfigureAwait(false);
        return ToModel(dbUser, defaultAuthor, isOnline);
    }

    /// <inheritdoc />
    [CommandHandler]
    public virtual async Task<AuthorId> CreateAuthor(IAuthorService.CreateAuthorCommand command, CancellationToken cancellationToken)
    {
        var (userId, chatId) = command;
        if (Computed.IsInvalidating()) {
            _ = GetByUserIdAndChatId(userId, chatId, default);
            return default;
        }
        DbAuthor? dbAuthor;
        if (!userId.IsNone) {
            var defaultAuthor = await _defaultAuthorService.Get(userId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The default author service returned <null>. " +
                    $"The userId: {userId} should have a default author.");
            dbAuthor = new DbAuthor(defaultAuthor);
        }
        else {
            var nickname = await _nicknameGenerator.Generate(cancellationToken).ConfigureAwait(false);
            dbAuthor = new DbAuthor() {
                IsAnonymous = true,
                Name = "Anonymous",
                Nickname = nickname,
                Picture = "//eu.ui-avatars.com/api/?background=random&bold=true&length=1&name=" + nickname,
            };
        }
        var dbContext = await CreateCommandDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbUser = new DbChatUser() {
            Author = dbAuthor,
            ChatId = chatId,
            UserId = userId.IsNone ? null : (string)userId,
        };

        await dbContext.AddAsync(dbUser, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbAuthor.Id;
    }

    private static Author ToModel(DbChatUser user, IAuthorInfo defaultAuthor, bool isOnline) => new() {
        AuthorId = user.AuthorId ?? AuthorId.None,
        UserId = user.UserId ?? UserId.None,
        Name = user.Author.Name ?? defaultAuthor.Name,
        Nickname = user.Author.Nickname ?? defaultAuthor.Nickname,
        Picture = user.Author.Picture ?? defaultAuthor.Picture,
        /// <see cref="IAuthorInfo.IsAnonymous"/> isn't inherited from parent author
        IsAnonymous = user.Author.IsAnonymous,
        // TODO: add user statuses
        IsOnline = isOnline,
    };
}
