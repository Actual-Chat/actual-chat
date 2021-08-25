using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.Extensions.DependencyInjection;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;

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
                var session = Services.GetRequiredService<ISessionFactory>().CreateSession();
                UserConstants.AdminSession = session;

                // Creating admin user
                var user = new User("", "Admin").WithIdentity(new UserIdentity("internal", "admin"));
                await auth.SignIn(
                    new SignInCommand(session, user, user.Identities.Keys.Single()).MarkServerSide(),
                    cancellationToken);
                user = await auth.GetUser(session, cancellationToken);
                UserConstants.AdminUserId = user.Id;
            }
        }
    }
}
