using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Module;

public class UsersDbInitializer : DbInitializer<UsersDbContext>
{
    public UsersDbInitializer(IServiceProvider services) : base(services) { }

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        await base.Initialize(cancellationToken).ConfigureAwait(false);
        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<UsersDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (DbInfo.ShouldRecreateDb) {
            var authBackend = Services.GetRequiredService<IAuthBackend>();
            var sessionFactory = Services.GetRequiredService<ISessionFactory>();

            // Creating admin user
            var adminIdentity = new UserIdentity("internal", "admin");
            await dbContext.Users.AddAsync(new DbUser() {
                Id = UserConstants.Admin.UserId,
                Name = "Admin",
                Identities = {
                    new DbUserIdentity<string>() {
                        DbUserId = UserConstants.Admin.UserId,
                        Id = adminIdentity.Id,
                        Secret = "",
                    },
                },
            }, cancellationToken).ConfigureAwait(false);
            await dbContext.UserAuthors.AddAsync(new DbUserAuthor() {
                UserId = UserConstants.Admin.UserId,
                Name = UserConstants.Admin.Name,
                Picture = UserConstants.Admin.Picture,
            }, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
