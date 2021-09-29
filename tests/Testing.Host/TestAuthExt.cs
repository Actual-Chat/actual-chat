using ActualChat.Host;
using Microsoft.Extensions.DependencyInjection;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Testing.Host
{
    public static class TestAuthExt
    {
        public static Task<User> SignIn(this IWebTester tester,
            User user,
            CancellationToken cancellationToken = default)
            => tester.AppHost.SignIn(tester.Session, user, cancellationToken);

        public static async Task<User> SignIn(
            this AppHost appHost,
            Session session, User user,
            CancellationToken cancellationToken = default)
        {
            if (!user.Identities.Any())
                user = user.WithIdentity(new UserIdentity("test", Ulid.NewUlid().ToString()));
            var userIdentity = user.Identities.Keys.First();

            var auth = appHost.Services.GetRequiredService<IServerSideAuthService>();
            var command = new SignInCommand(session, user, userIdentity).MarkServerSide();
            await auth.SignIn(command, cancellationToken);
            var sessionInfo = await auth.GetSessionInfo(session, cancellationToken);
            sessionInfo.MustBeAuthenticated();
            user = (await auth.TryGetUser(sessionInfo.UserId, cancellationToken))!;
            return user;
        }
    }
}
