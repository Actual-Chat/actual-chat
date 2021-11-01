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

        if (ShouldRecreateDb) {
            var auth = Services.GetRequiredService<IServerSideAuthService>();
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
                DefaultAuthor = new DbDefaultAuthor() {
                    UserId = UserConstants.Admin.AuthorId,
                    Name = UserConstants.Admin.Name,
                    Nickname = UserConstants.Admin.Nickname,
                    Picture = UserConstants.Admin.Picture,
                },
            }, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Signing in to admin session
            var session = sessionFactory.CreateSession();
            var user = await auth.TryGetUser(UserConstants.Admin.UserId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to create 'admin' user.");
            await auth.SignIn(
                new SignInCommand(session, user, user.Identities.Keys.Single()).MarkServerSide(),
                cancellationToken).ConfigureAwait(false);
            UserConstants.Admin.Session = session;
        }
    }
}
