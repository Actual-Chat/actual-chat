using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Testing
{
    public static class TestAuthEx
    {
        public static async Task<User> SignIn(
            this BlazorTester blazorTester,
            User user, CancellationToken cancellationToken = default)
        {
            var session = blazorTester.Session;
            var auth = blazorTester.Auth;

            if (!user.Identities.Any())
                user = user.WithIdentity(new UserIdentity("test", Ulid.NewUlid().ToString()));
            var userIdentity = user.Identities.Keys.First();
            var signInCommand = new SignInCommand(session, user, userIdentity).MarkServerSide();

            await auth.SignIn(signInCommand, cancellationToken);
            return await auth.GetUser(session, cancellationToken);
        }
    }
}
