using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAuthorsService : DbServiceBase<UsersDbContext>, IUserAuthorsBackend
{
    private readonly IUserInfos _userInfos;

    public UserAuthorsService(IUserInfos userInfos, IServiceProvider services)
        : base(services)
        => _userInfos = userInfos;

    // Backend

    // [ComputeMethod]
    public virtual async Task<UserAuthor?> Get(UserId userId, bool inherit, CancellationToken cancellationToken)
    {
        if (userId == UserId.None)
            return null;

        UserAuthor? userAuthor;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbUserAuthor = await dbContext.UserAuthors
                .SingleOrDefaultAsync(a => a.UserId == (string)userId, cancellationToken)
                .ConfigureAwait(false);
            userAuthor = dbUserAuthor?.ToModel();
        }
        if (!inherit)
            return userAuthor;

        var userInfo = await _userInfos.Get(userId, cancellationToken).ConfigureAwait(false);
        return userAuthor.InheritFrom(userInfo);
    }
}
