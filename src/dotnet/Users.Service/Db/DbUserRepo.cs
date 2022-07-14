using ActualChat.Users.Module;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserRepo : DbUserRepo<UsersDbContext, DbUser, string>
{
    private readonly UsersSettings _usersSettings;

    public DbUserRepo(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
        : base(options, services)
        => _usersSettings = services.GetRequiredService<UsersSettings>();

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
            Status = isAdmin ? AccountStatus.Active : _usersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
        };
        dbContext.Accounts.Add(dbAccount);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
