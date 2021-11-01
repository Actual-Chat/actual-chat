using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class DefaultAuthorService : DbServiceBase<UsersDbContext>, IDefaultAuthorService
{
    public DefaultAuthorService(IServiceProvider services) : base(services) { }

    [ComputeMethod(KeepAliveTime = 10)]
    public virtual async Task<IAuthorInfo?> Get(UserId userId, CancellationToken cancellationToken)
    {
        DefaultAuthor? result = null;
        if (userId != UserId.None) {
            using var db = CreateDbContext(readWrite: false);
            var user = await db.Users
                .Include(u => u.DefaultAuthor)
                .FirstOrDefaultAsync(u => u.Id == (string)userId, cancellationToken)
                .ConfigureAwait(false);

            result = user?.DefaultAuthor?.ToModel();
        }
        return result;
    }
}
