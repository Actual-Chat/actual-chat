using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Module;

public class UsersDbInitializer : DbInitializer<UsersDbContext>
{
    private HostInfo HostInfo { get; init; } = null!;
    public UsersDbInitializer(IServiceProvider services, HostInfo hostInfo) : base(services)
        => HostInfo = hostInfo;

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        await base.Initialize(cancellationToken).ConfigureAwait(false);
        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<UsersDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var admin = await dbContext.Users
            .Get(Constants.User.Admin.UserId, cancellationToken)
            .ConfigureAwait(false);
        var isNewDb = HostInfo.IsDevelopmentInstance && (DbInfo.ShouldRecreateDb || admin is null);
        if (isNewDb) {
            Log.LogInformation("Initializing users DB...");
            var commander = Services.Commander();
            var authBackend = Services.GetRequiredService<IAuthBackend>();
            var sessionFactory = Services.GetRequiredService<ISessionFactory>();

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
            var session = sessionFactory.CreateSession();
            var adminUser = await authBackend.GetUser(default, Constants.User.Admin.UserId, cancellationToken).ConfigureAwait(false)
                ?? throw StandardError.Internal("Failed to locate 'admin' user.");
            await commander.Call(
                    new SignInCommand(session, adminUser, adminUser.Identities.Keys.Single()),
                    cancellationToken)
                .ConfigureAwait(false);

            // Setting default avatar for admin
            var serverKvasBackend = Services.GetRequiredService<IServerKvasBackend>();
            var adminKvas = serverKvasBackend.GetUserClient(adminUser);
            var adminAvatarSettings = new UserAvatarSettings() {
                DefaultAvatarId = avatarId,
                AvatarIds = ImmutableArray.Create((Symbol)avatarId),
            };
            await adminKvas.SetUserAvatarSettings(adminAvatarSettings, cancellationToken).ConfigureAwait(false);

            await AddUsers(dbContext, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddUsers(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        for (int i = 1; i < 30; i++) {
            var userId = $"user{i:00}";
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
