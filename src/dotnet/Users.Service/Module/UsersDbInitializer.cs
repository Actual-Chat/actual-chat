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
            var authBackend = Services.GetRequiredService<IAuthBackend>();
            var sessionFactory = Services.GetRequiredService<ISessionFactory>();

            // Creating admin user
            var adminIdentity = new UserIdentity("internal", "admin");
            dbContext.Users.Add(new DbUser() {
                Id = UserConstants.Admin.UserId,
                Name = "Admin",
                Identities = {
                    new DbUserIdentity<string>() {
                        DbUserId = UserConstants.Admin.UserId,
                        Id = adminIdentity.Id,
                        Secret = "",
                    },
                },
            });
            var avatarId = Ulid.NewUlid().ToString();
            dbContext.UserAvatars.Add(new DbUserAvatar() {
                Id = avatarId,
                UserId = UserConstants.Admin.UserId,
                Name = UserConstants.Admin.Name,
                Picture = UserConstants.Admin.Picture,
            });

            dbContext.UserAuthors.Add(new DbUserAuthor() {
                UserId = UserConstants.Admin.UserId,
                Name = UserConstants.Admin.Name,
                AvatarId = avatarId,
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
            var user = await authBackend.GetUser(UserConstants.Admin.UserId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to create 'admin' user.");
            await authBackend.SignIn(
                new SignInCommand(session, user, user.Identities.Keys.Single()),
                cancellationToken).ConfigureAwait(false);
            UserConstants.Admin.Session = session;
        }
    }
}
