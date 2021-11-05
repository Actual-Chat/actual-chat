using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserRepository : DbUserRepo<UsersDbContext, DbUser, string>
{
    public DbUserRepository(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
        : base(options, services) { }

    public override async Task<DbUser> Create(UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        var dbUser = await base.Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        var dbUserAuthor = new DbUserAuthor();
        var userAuthor = new UserAuthor() {
            Id = dbUser.Id,
            Name = user.Name,
        };
        dbUserAuthor.UpdateFrom(userAuthor);
        await dbContext.AddAsync(dbUserAuthor, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
