using ActualChat.Users.Module;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserRepo : DbUserRepo<UsersDbContext, DbUser, string>
{
    private UsersSettings UsersSettings { get; }

    public DbUserRepo(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
        : base(options, services)
        => UsersSettings = services.GetRequiredService<UsersSettings>();

    public override async Task<DbUser> Create(
        UsersDbContext dbContext,
        User user,
        CancellationToken cancellationToken = default)
    {
        var dbUser = await base.Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        user = UserConverter.ToModel(dbUser);

        var isAdmin = AccountsBackend.IsAdmin(user);
        var dbAccount = new DbAccount {
            Id = user.Id,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
        };
        dbContext.Accounts.Add(dbAccount);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
