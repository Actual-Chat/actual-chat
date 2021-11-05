using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAuthorsService : DbServiceBase<UsersDbContext>, IUserAuthors
{
    public UserAuthorsService(IServiceProvider services) : base(services) { }

    // [ComputeMethod]
    public virtual async Task<UserAuthor?> Get(UserId userId, CancellationToken cancellationToken)
    {
        if (userId == UserId.None)
            return null;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var userAuthor = await dbContext.UserAuthors
            .SingleOrDefaultAsync(a => a.UserId == (string)userId, cancellationToken)
            .ConfigureAwait(false);
        return userAuthor?.ToModel();
    }
}
