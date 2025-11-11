using System.Security.Claims;
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
        var name = user.Claims.GetValueOrDefault(ClaimTypes.GivenName, "");
        var lastName = user.Claims.GetValueOrDefault(ClaimTypes.Surname, "");
        if (!lastName.IsNullOrEmpty())
            name = $"{name} {lastName}";
        var dbAccount = new DbAccount {
            Id = user.Id,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
            Name = name,
            Email = user.Claims.GetValueOrDefault(ClaimTypes.Email, ""),
            Phone = user.Claims.GetValueOrDefault(ClaimTypes.MobilePhone, ""),
            CreatedAt = dbUser.CreatedAt,
        };
        dbContext.Accounts.Add(dbAccount);

        var emailString = dbAccount.Email;
        if (!emailString.IsNullOrEmpty() && ActualChat.Email.TryParse(emailString, out var email)) {
            user = user.WithEmailIdentities(email);
            UserConverter.UpdateEntity(user, dbUser);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(
            new AccountChangedEvent(dbAccount.ToModel(user), null, ChangeKind.Create));
        return dbUser;
    }
}
