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
        AccountFull account,
        CancellationToken cancellationToken = default)
        => tester.AppHost.SignIn(tester.Session, account, cancellationToken);

    public static Task<AccountFull> SignInAsNew(
        this IWebTester tester,
        string namePrefix,
        CancellationToken cancellationToken = default)
        => tester.SignInAsNew(namePrefix, null, cancellationToken);

    public static Task<AccountFull> SignInAsNew(
        this IWebTester tester,
        string namePrefix,
        Func<AccountFull, AccountFull>? setup,
        CancellationToken cancellationToken = default)
    {
        var account = NewAccount(namePrefix);
        return tester.AppHost.SignIn(tester.Session, setup?.Invoke(account) ?? account, cancellationToken);
    }

    public static AccountFull NewAccount(string name)
        => new AccountFull(name).WithClaim(ClaimTypes.GivenName, name);

    public static async Task<AccountFull> SignIn(
        this AppHost appHost,
        Session session,
        AccountFull account,
        CancellationToken cancellationToken = default)
    {
        var services = appHost.Services;
        var userIdentity = account.Identities.IsEmpty
            ? new UserIdentity("test", Ulid.NewUlid().ToString()!)
            : account.Identities.Keys.First();
        var newIdentities = account.Identities.IsEmpty
            ? ApiMap<UserIdentity, string>.Empty
            : account.Identities.Where(x => x.Key != userIdentity).ToApiMap(x => x.Key, x => x.Value);
        var commander = services.Commander();
        var accounts = services.GetRequiredService<IAccounts>();

        var command = new AccountsBackend_SignIn(session, userIdentity, newIdentities, account.Claims);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await WaitTillAuthenticationHappened(accounts, session, userIdentity, cancellationToken).ConfigureAwait(false);
    }

    // TODO: (FC) Remove when AY takes a look at hanging Account.GetOwn on client side
    public static async Task<AccountFull> SignInClientSide(
        this IWebClientTester tester,
        AccountFull account,
        CancellationToken cancellationToken = default)
    {
        var userIdentity = account.Identities.IsEmpty
            ? new UserIdentity("test", Ulid.NewUlid().ToString()!)
            : account.Identities.Keys.First();
        var newIdentities = account.Identities.IsEmpty
            ? ApiMap<UserIdentity, string>.Empty
            : account.Identities.Where(x => x.Key != userIdentity).ToApiMap(x => x.Key, x => x.Value);
        var commander = tester.AppServices.Commander();

        var session = tester.Session;
        var command = new AccountsBackend_SignIn(session, userIdentity, newIdentities, account.Claims);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);

        var accounts = tester.ClientServices.GetRequiredService<IAccounts>();
        // TODO(FC): remove this debug line after AY
        var accountFull = await accounts.GetOwn(tester.Session, cancellationToken);
        return await WaitTillAuthenticationHappened(accounts, session, userIdentity, cancellationToken).ConfigureAwait(false);
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

        var command = new Accounts_SignOut(session, force);
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

    private static async Task<AccountFull> WaitTillAuthenticationHappened(
        IAccounts accounts,
        Session session,
        UserIdentity userIdentity,
        CancellationToken cancellationToken)
    {
        var cAccount = await Computed
            .Capture(() => accounts.GetOwn(session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull() && x.Identities.Keys.Contains(userIdentity), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
            .ConfigureAwait(false);
        return cAccount.Value;
    }

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
