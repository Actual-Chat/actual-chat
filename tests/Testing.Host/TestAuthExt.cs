using ActualChat.App.Server;
using ActualChat.Commands;
using ActualChat.Commands.Internal;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.Playwright;
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

        var sessionInfo = await auth.GetSessionInfo(session, cancellationToken)
            .Require(SessionInfo.MustBeAuthenticated)
            .ConfigureAwait(false);
        user = (await authBackend.GetUser(default, sessionInfo.UserId, cancellationToken).ConfigureAwait(false))!;

        // Let's wait a bit to ensure all invalidations go through
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);

        var localCommandQueues = services.GetRequiredService<ICommandQueues>();
        var localCommandQueue = localCommandQueues.Get(Queues.Users) as LocalCommandQueue;
        while (localCommandQueue!.Commands.Reader.TryPeek(out _))
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);

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

    public static async Task ClientSignInWithGoogle(
        this IPage page,
        string user,
        string password)
        => await ClientSignInWith(GoogleDefaults.AuthenticationScheme, user, password, page);

    private static async Task ClientSignInWith(string authScheme, string user, string password, IPage page)
    {
        await StartClientSignInWith(authScheme, page);

        switch (authScheme) {
            case GoogleDefaults.AuthenticationScheme:
                await HandleGoogleSignInPopup(user, password, page);
                break;
            case MicrosoftAccountDefaults.AuthenticationScheme:
                throw new NotImplementedException();
            default:
                throw new ArgumentOutOfRangeException(nameof(authScheme));
        }

        await page.WaitForSelectorAsync("button :text(\"actualchat_testuser1\")");
    }

    private static async Task StartClientSignInWith(string scheme, IPage page)
    {
        var continueWithGoogleBtn = await page.WaitForSelectorAsync("button :text(\"Continue with Google\")");
        continueWithGoogleBtn.Should().NotBeNull();
        await continueWithGoogleBtn!.ClickAsync();
    }

    private static async Task HandleGoogleSignInPopup(string user, string password, IPage page)
    {
        var googlePage = await page.Context.WaitForPageAsync(new BrowserContextWaitForPageOptions
        {
            Predicate = x => new Uri(x.Url).Host == "accounts.google.com"
        });

        var emailInput = await googlePage.WaitForSelectorAsync("input[type=email][name=identifier]");
        emailInput.Should().NotBeNull();
        await emailInput!.FillAsync(user);
        await emailInput.PressAsync("Enter");


        var passwordInput = await googlePage.WaitForSelectorAsync("input[type=password][name=password]");
        passwordInput.Should().NotBeNull();
        await passwordInput!.FillAsync(password);
        await passwordInput.PressAsync("Enter");
    }
}
