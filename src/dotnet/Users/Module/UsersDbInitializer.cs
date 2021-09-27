using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Module
{
    public class UsersDbInitializer : DbInitializer<UsersDbContext>
    {
        public UsersDbInitializer(IServiceProvider services) : base(services) { }

        public override async Task Initialize(CancellationToken cancellationToken)
        {
            await base.Initialize(cancellationToken);
            if (ShouldRecreateDb) {
                var auth = Services.GetRequiredService<IServerSideAuthService>();
                var sessionFactory = Services.GetRequiredService<ISessionFactory>();
                var dbContextFactory = Services.GetRequiredService<IDbContextFactory<UsersDbContext>>();

                // Creating admin user
                await using var dbContext = dbContextFactory.CreateDbContext().ReadWrite();
                var adminIdentity = new UserIdentity("internal", "admin");
                dbContext.Users.Add(new DbUser() {
                    Id = UserConstants.AdminUserId,
                    Name = "Admin",
                    Identities = {
                        new DbUserIdentity<string>() {
                            DbUserId = UserConstants.AdminUserId,
                            Id = adminIdentity.Id,
                            Secret = "",
                        },
                    }
                });
                await dbContext.SaveChangesAsync(cancellationToken);

                // Signing in to admin session
                var session = sessionFactory.CreateSession();
                var user = await auth.TryGetUser(UserConstants.AdminUserId, cancellationToken)
                    ?? throw new InvalidOperationException("Failed to create 'admin' user.");
                await auth.SignIn(
                    new SignInCommand(session, user, user.Identities.Keys.Single()).MarkServerSide(),
                    cancellationToken);
                UserConstants.AdminSession = session;
            }
        }
    }
}
