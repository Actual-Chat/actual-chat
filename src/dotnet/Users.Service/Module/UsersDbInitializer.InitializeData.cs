using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Module;

public partial class UsersDbInitializer
{
    protected override async Task InitializeData(CancellationToken cancellationToken)
    {
        var dbContext = DbHub.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);

        Log.LogInformation("Initializing data...");

        await EnsureAdminUserExists(dbContext, cancellationToken).ConfigureAwait(false);
        if (HostInfo.IsDevelopmentInstance)
            await EnsureExtraUsersExist(dbContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAdminUserExists(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        var adminExists = await dbContext.Users
            .AnyAsync(u => u.Id == Constants.User.Admin.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (adminExists)
            return;

        Log.LogInformation("Creating admin user...");

        var commander = Services.Commander();

        // Creating admin user
        var adminIdentity = new UserIdentity("internal", Constants.User.Admin.UserId);
        dbContext.Users.Add(new DbUser() {
            Id = Constants.User.Admin.UserId,
            Name = Constants.User.Admin.Name,
            Identities = {
                new DbUserIdentity<string>() {
                    DbUserId = Constants.User.Admin.UserId,
                    Id = adminIdentity.Id,
                    Secret = "",
                },
            },
        });
        var avatarId = Ulid.NewUlid().ToString();
        dbContext.Accounts.Add(new DbAccount {
            Id = Constants.User.Admin.UserId,
            Status = AccountStatus.Active,
        });
        dbContext.Avatars.Add(new DbAvatar() {
            Id = avatarId,
            UserId = Constants.User.Admin.UserId,
            Name = Constants.User.Admin.Name,
            Picture = Constants.User.Admin.Picture,
        });

        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) {
            // Looks like we're starting w/ existing DB
            Log.LogWarning(e, "Couldn't create 'admin' user, looks like we're starting w/ existing DB");
            dbContext.ChangeTracker.Clear();
        }

        // Signing in to admin session
        var session = Services.GetRequiredService<ISessionFactory>().CreateSession();
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
        var admin = await accountsBackend.Get(Constants.User.Admin.UserId, cancellationToken).ConfigureAwait(false);
        admin = admin.Require(AccountFull.MustBeAdmin);

        var signInCommand = new SignInCommand(session, admin.User, admin.User.Identities.Keys.Single());
        await commander.Call(signInCommand, cancellationToken).ConfigureAwait(false);

        // Setting default avatar for admin
        var serverKvasBackend = Services.GetRequiredService<IServerKvasBackend>();
        var adminKvas = serverKvasBackend.GetUserClient(admin);
        var adminAvatarSettings = new UserAvatarSettings() {
            DefaultAvatarId = avatarId,
            AvatarIds = ImmutableArray.Create((Symbol)avatarId),
        };
        await adminKvas.SetUserAvatarSettings(adminAvatarSettings, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureExtraUsersExist(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        Log.LogInformation("Creating extra users...");

        for (int i = 1; i < 30; i++) {
            var userId = $"user{i:00}";
            var dbUser = await dbContext.Users.Get(userId, cancellationToken).ConfigureAwait(false);
            if (dbUser != null)
                continue;

            var userName = $"User_{i:00}";
            var userIdentity = new UserIdentity("internal", userId);
            dbContext.Users.Add(new DbUser() {
                Id = userId,
                Name = userName,
                Identities = {
                    new DbUserIdentity<string>() {
                        DbUserId = userId,
                        Id = userIdentity.Id,
                        Secret = "",
                    },
                },
            });
            dbContext.Accounts.Add(new DbAccount {
                Id = userId,
                Status = AccountStatus.Active,
            });
            Log.LogInformation("+ {UserId}: {UserName}", userId, userName);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
