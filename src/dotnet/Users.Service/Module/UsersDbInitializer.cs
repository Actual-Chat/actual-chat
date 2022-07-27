using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Module;

public class UsersDbInitializer : DbInitializer<UsersDbContext>
{
    public UsersDbInitializer(IServiceProvider services) : base(services) { }

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        await base.Initialize(cancellationToken).ConfigureAwait(false);
        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<UsersDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        if (DbInfo.ShouldRecreateDb) {
            Log.LogInformation("Recreating DB...");
            var commander = Services.Commander();
            var authBackend = Services.GetRequiredService<IAuthBackend>();
            var sessionFactory = Services.GetRequiredService<ISessionFactory>();

            // Creating admin user
            var adminIdentity = new UserIdentity("internal", "admin");
            dbContext.Users.Add(new DbUser() {
                Id = UserConstants.Admin.UserId,
                Name = UserConstants.Admin.Name,
                Identities = {
                    new DbUserIdentity<string>() {
                        DbUserId = UserConstants.Admin.UserId,
                        Id = adminIdentity.Id,
                        Secret = "",
                    },
                },
            });
            var avatarId = Ulid.NewUlid().ToString();
            dbContext.Accounts.Add(new DbAccount {
                Id = UserConstants.Admin.UserId,
                Status = AccountStatus.Active,
                AvatarId = avatarId,
            });
            dbContext.UserAvatars.Add(new DbUserAvatar() {
                Id = avatarId,
                UserId = UserConstants.Admin.UserId,
                Name = UserConstants.Admin.Name,
                Picture = UserConstants.Admin.Picture,
            });

            try {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) {
                // Looks like we're starting w/ existing DB
                dbContext.ChangeTracker.Clear();
            }

            // Signing in to admin session
            var session = sessionFactory.CreateSession();
            var user = await authBackend.GetUser(default, UserConstants.Admin.UserId, cancellationToken).ConfigureAwait(false)
                ?? throw StandardError.Internal("Failed to create 'admin' user.");
            await commander.Call(
                    new SignInCommand(session, user, user.Identities.Keys.Single()),
                    cancellationToken)
                .ConfigureAwait(false);
            UserConstants.Admin.Session = session;

            await AddUsers(dbContext, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddUsers(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        for (int i = 1; i < 30; i++) {
            var userId = $"user{i}";
            var userName = $"User_{i}";
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
            var avatarId = Ulid.NewUlid().ToString();
            dbContext.Accounts.Add(new DbAccount {
                Id = userId,
                Status = AccountStatus.Active,
                AvatarId = avatarId,
            });
            dbContext.UserAvatars.Add(new DbUserAvatar() {
                Id = avatarId,
                UserId = userId,
                Name = userName,
                Picture = UserConstants.Admin.Picture,
            });

            try {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) {
                // Looks like we're starting w/ existing DB
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
