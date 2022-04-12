using ActualChat.Host;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Testing.Host;

public static class TestAuthExt
{
    public static Task<User> SignIn(
        this IWebTester tester,
        User user,
        CancellationToken cancellationToken = default)
        => tester.AppHost.SignIn(tester.Session, user, cancellationToken);

    public static async Task<User> SignIn(
        this AppHost appHost,
        Session session,
        User user,
        CancellationToken cancellationToken = default)
    {
        if (!user.Identities.Any())
            user = user.WithIdentity(new UserIdentity("test", Ulid.NewUlid().ToString()));
        var userIdentity = user.Identities.Keys.First();

        var services = appHost.Services;
        var commander = services.Commander();
        var auth = services.GetRequiredService<IAuth>();
        var authBackend = services.GetRequiredService<IAuthBackend>();

        var command = new SignInCommand(session, user, userIdentity);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
        var sessionInfo = await auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        sessionInfo = sessionInfo.MustBeAuthenticated();
        user = (await authBackend.GetUser(sessionInfo.UserId, cancellationToken).ConfigureAwait(false))!;

        // Let's wait a bit to ensure all invalidations go through
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        return user;
    }

    public static Task SignOut(
        this IWebTester tester,
        bool force = false,
        CancellationToken cancellationToken = default)
        => tester.AppHost.SignOut(tester.Session, force, cancellationToken);

    public static async Task SignOut(
        this AppHost appHost,
        Session session,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var services = appHost.Services;
        var commander = services.Commander();

        var command = new SignOutCommand(session, force);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);

        // Let's wait a bit to ensure all invalidations go through
        // TODO: REALLY???
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
    }
}
