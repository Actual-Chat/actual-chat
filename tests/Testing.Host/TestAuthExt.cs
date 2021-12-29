using ActualChat.Host;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Testing.Host;

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

        var auth = appHost.Services.GetRequiredService<IAuth>();
        var authBackend = appHost.Services.GetRequiredService<IAuthBackend>();
        var command = new SignInCommand(session, user, userIdentity);
        await authBackend.SignIn(command, cancellationToken).ConfigureAwait(false);
        var sessionInfo = await auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        sessionInfo = sessionInfo.MustBeAuthenticated();
        user = (await authBackend.GetUser(sessionInfo.UserId, cancellationToken).ConfigureAwait(false))!;
        return user;
    }
}

