using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAuthorsBackend : DbServiceBase<UsersDbContext>, IUserAuthorsBackend
{
    private readonly IUserProfilesBackend _userProfilesBackend;
    private readonly IUserAvatarsBackend _userAvatarsBackend;
    private readonly IDbEntityResolver<string, DbUserAuthor> _dbUserAuthorResolver;

    public UserAuthorsBackend(IServiceProvider services) : base(services)
    {
        _userProfilesBackend = Services.GetRequiredService<IUserProfilesBackend>();
        _userAvatarsBackend = Services.GetRequiredService<IUserAvatarsBackend>();
        _dbUserAuthorResolver = Services.GetRequiredService<IDbEntityResolver<string, DbUserAuthor>>();
    }

    // [ComputeMethod]
    public virtual async Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return null;

        var dbUserAuthor = await _dbUserAuthorResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        var userAuthor = dbUserAuthor?.ToModel();
        if (!inherit || userAuthor == null)
            return userAuthor;

        // Note that inherit == true here
        if (!userAuthor.AvatarId.IsEmpty) {
            var avatar = await _userAvatarsBackend.Get(userAuthor.AvatarId, cancellationToken).ConfigureAwait(false);
            var result = userAuthor;
            if (avatar != null) {
                result = result with {Name = avatar.Name, Picture = avatar.Picture};
            }
            return result;
        }
        else {
            var userProfile = await _userProfilesBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            var result = userAuthor.InheritFrom(userProfile);
            return result;
        }
    }

    // [CommandHandler]
    public virtual async Task SetAvatar(IUserAuthorsBackend.SetAvatarCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;

        if (Computed.IsInvalidating()) {
            _ = Get(userId, false, default);
            _ = Get(userId, true, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserAuthor= await dbContext.UserAuthors
            .SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserAuthor == null)
            throw new InvalidOperationException("user author does not exists");

        dbUserAuthor.AvatarId = command.AvatarId;
        dbUserAuthor.Version = VersionGenerator.NextVersion();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
