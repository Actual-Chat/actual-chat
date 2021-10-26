using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <inheritdoc cref="IAuthorService"/>
public class AuthorService : DbServiceBase<ChatDbContext>, IAuthorService
{
    private readonly IDefaultAuthorService _defaultAuthorService;
    private readonly IUserStateService _userStateService;

    public AuthorService(
        IDefaultAuthorService defaultAuthorService,
        IUserStateService userStateService,
        IServiceProvider serviceProvider
    ) : base(serviceProvider)
    {
        _defaultAuthorService = defaultAuthorService;
        _userStateService = userStateService;
    }

    /// <inheritdoc />
    public virtual async Task<Author> GetByAuthorId(AuthorId authorId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext(readWrite: false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAuthor = await dbContext.Authors
            .FirstOrDefaultAsync(x => x.Id == (string)authorId, cancellationToken).ConfigureAwait(false);
        var defaultAuthor = await _defaultAuthorService.Get(dbAuthor?.UserId ?? UserId.None, cancellationToken)
            .ConfigureAwait(false);
        if (dbAuthor == null)
            return new(defaultAuthor);
        var status = await _userStateService.IsOnline(dbAuthor.UserId ?? "", cancellationToken).ConfigureAwait(false);
        return Merge(dbAuthor, defaultAuthor, status);
    }

    /// <inheritdoc />
    public virtual async Task<Author> GetByUserId(UserId userId, CancellationToken cancellationToken)
    {
        var defaultAuthor = await _defaultAuthorService.Get(userId, cancellationToken).ConfigureAwait(false);
        if (userId.IsNone)
            return new(defaultAuthor);

        var dbContext = CreateDbContext(readWrite: false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAuthor = await dbContext.Authors
            .FirstOrDefaultAsync(x => x.UserId == (string)userId, cancellationToken).ConfigureAwait(false);
        if (dbAuthor == null)
            return new(defaultAuthor);

        var status = await _userStateService.IsOnline(userId, cancellationToken).ConfigureAwait(false);
        return Merge(dbAuthor, defaultAuthor, status);
    }

    /// <inheritdoc />
    [CommandHandler, Internal]
    public virtual async Task<AuthorId> CreateAuthor(IAuthorService.CreateAuthorCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Computed.IsInvalidating()) {
            _ = GetByUserId(userId, default);
            return default;
        }
        var defaultAuthor = await _defaultAuthorService.Get(userId, cancellationToken).ConfigureAwait(false);
        var dbContext = CreateDbContext(readWrite: true);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbAuthor = new DbAuthor(defaultAuthor) {
            Id = Ulid.NewUlid().ToString(),
        };
        dbContext.Add(dbAuthor);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbAuthor.Id;
    }

    private static Author Merge(DbAuthor dbAuthor, IAuthorInfo defaultAuthor, bool status) => new() {
        UserId = dbAuthor.UserId ?? UserId.None,
        Name = dbAuthor.Name ?? defaultAuthor.Name,
        Nickname = dbAuthor.Nickname ?? defaultAuthor.Nickname,
        Picture = dbAuthor.Picture ?? defaultAuthor.Picture,
        /// <see cref="IAuthorInfo.IsAnonymous"/> isn't inherited from parent author
        IsAnonymous = dbAuthor.IsAnonymous,
        // TODO: add user statuses
        IsOnline = status,
    };
}
