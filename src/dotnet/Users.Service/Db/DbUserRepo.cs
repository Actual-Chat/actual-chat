using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserRepo : DbUserRepo<UsersDbContext, DbUser, string>
{
    public new IDbEntityConverter<DbUser, User> UserConverter { get; }

    public DbUserRepo(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
        : base(options, services)
        => UserConverter = services.GetRequiredService<IDbEntityConverter<DbUser, User>>();

    public override async Task<DbUser> Create(
        UsersDbContext dbContext,
        User user,
        CancellationToken cancellationToken = default)
    {
        var dbUser = await base.Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        var dbUserAuthor = new DbUserAuthor();
        var userAuthor = new UserAuthor() {
            Id = dbUser.Id,
            Name = user.Name,
        };
        dbUserAuthor.UpdateFrom(userAuthor);
        dbContext.Add(dbUserAuthor);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
