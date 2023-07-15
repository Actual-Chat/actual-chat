using ActualChat.App.Server;
using ActualChat.Commands;
using ActualChat.Commands.Internal;
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

    public static async Task<AccountFull> SignIn(
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
        var accounts = services.GetRequiredService<IAccounts>();

        var command = new AuthBackend_SignIn(session, user, userIdentity);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);

        // Wait till the authentication happens
        var cAccount = await Computed.Capture(() => accounts.GetOwn(session, cancellationToken)).ConfigureAwait(false);
        cAccount = await cAccount
            .Changes(FixedDelayer.ZeroUnsafe, cancellationToken)
            .FirstAsync(c => !(c.ValueOrDefault?.IsGuestOrNone ?? true), cancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
            .ConfigureAwait(false);
        var account = cAccount.Value;

        // Just in case
        await Task.Delay(TimeSpan.FromSeconds(0.1)).ConfigureAwait(false);
        return account;
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
