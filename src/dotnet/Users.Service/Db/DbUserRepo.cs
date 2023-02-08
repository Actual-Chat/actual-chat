using System.Security.Claims;
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
            Name = user.Claims.TryGetValue(ClaimTypes.GivenName, out var name) ? name : "",
            LastName = user.Claims.TryGetValue(ClaimTypes.Surname, out var surname) ? surname : "",
            Email = user.Claims.TryGetValue(ClaimTypes.Email, out var email) ? email : "",
        };
        dbContext.Accounts.Add(dbAccount);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
