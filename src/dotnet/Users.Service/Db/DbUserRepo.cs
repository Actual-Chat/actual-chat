using System.Security.Claims;
using ActualChat.Queues;
using ActualChat.Users.Events;
using ActualChat.Users.Module;
using ActualLab.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

public class DbUserRepo(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
    : DbUserRepo<UsersDbContext, DbUser, string>(options, services)
{
    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();

    public override async Task<DbUser> Create(
        UsersDbContext dbContext,
        User user,
        CancellationToken cancellationToken = default)
    {
        var dbUser = await base.Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        user = UserConverter.ToModel(dbUser);

        var context = CommandContext.GetCurrent();
        var isAdmin = AccountsBackend.IsAdmin(user);
        var dbAccount = new DbAccount {
            Id = user.Id,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
            Name = user.Claims.GetValueOrDefault(ClaimTypes.GivenName, ""),
            LastName = user.Claims.GetValueOrDefault(ClaimTypes.Surname, ""),
            Email = user.Claims.GetValueOrDefault(ClaimTypes.Email, ""),
            Phone = user.Claims.GetValueOrDefault(ClaimTypes.MobilePhone, ""),
            CreatedAt = dbUser.CreatedAt,
        };
        var email = dbAccount.Email;
        dbContext.Accounts.Add(dbAccount);

        if (!email.IsNullOrEmpty()) {
            user = user.WithEmailIdentities(email);
            UserConverter.UpdateEntity(user, dbUser);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new AccountChangedEvent(dbAccount.ToModel(user), null, ChangeKind.Create));
        return dbUser;
    }
}
