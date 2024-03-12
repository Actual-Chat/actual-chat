using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.Playwright;

namespace ActualChat.Testing.Host;

public static class TestAuthExt
{
    public static Task<AccountFull> SignIn(
        this IWebTester tester,
        User user,
        CancellationToken cancellationToken = default)
        => tester.AppHost.SignIn(tester.Session, user, cancellationToken);

    public static Task<AccountFull> SignInAsNew(
        this IWebTester tester,
        string namePrefix,
        CancellationToken cancellationToken = default)
        => tester.AppHost.SignIn(tester.Session, new User("", UniqueNames.User(namePrefix)).WithClaim(ClaimTypes.GivenName, namePrefix), cancellationToken);

    public static async Task<AccountFull> SignIn(
        this AppHost appHost,
        Session session,
        User user,
        CancellationToken cancellationToken = default)
    {
        if (user.Identities.IsEmpty)
            user = user.WithIdentity(new UserIdentity("test", Ulid.NewUlid().ToString()!));
        var userIdentity = user.Identities.Keys.First();

        var services = appHost.Services;
        var commander = services.Commander();
        var accounts = services.GetRequiredService<IAccounts>();

        var command = new AuthBackend_SignIn(session, user, userIdentity);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);

        // Wait till the authentication happens
        var cAccount = await Computed
            .Capture(() => accounts.GetOwn(session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNone, FixedDelayer.ZeroUnsafe, cancellationToken)
            .ConfigureAwait(false);
        return cAccount.Value;
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

        var command = new Auth_SignOut(session, force);
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
                throw new NotSupportedException();
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
        var googlePage = await page.Context.WaitForPageAsync(new BrowserContextWaitForPageOptions {
            Predicate = x => x.Url.ToUri().Host == "accounts.google.com",
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
