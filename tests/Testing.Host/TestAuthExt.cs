using System.Security.Claims;
using ActualChat.App.Server;
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
        var accounts = services.GetRequiredService<IAccounts>();
        var sessionsBackend = services.GetRequiredService<ISessionsBackend>();
        var commander = services.Commander();

        var userIdentity = account.Identities.IsEmpty
            ? new UserIdentity("test", Ulid.NewUlid().ToString()!)
            : account.Identities.Keys.First();
        var newIdentities = account.Identities.IsEmpty
            ? new ApiMap<UserIdentity, string>()
            : account.Identities.Where(x => x.Key != userIdentity).ToApiMap(x => x.Key, x => x.Value);

        // Sign out first if already signed in
        var sessionInfo = await sessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo?.UserId is not null) {
            var signOutCommand = new AccountsBackend_SignOut(session);
            await commander.Call(signOutCommand, cancellationToken).ConfigureAwait(false);
            await WaitForSignOut(accounts, session, cancellationToken).ConfigureAwait(false);
        }

        var command = new AccountsBackend_SignIn(session, userIdentity, newIdentities, account.Claims, AutoCreate: true);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await WaitForSignIn(accounts, session, userIdentity, cancellationToken).ConfigureAwait(false);
    }

    // TODO: (FC) Remove when AY takes a look at hanging Account.GetOwn on client side
    public static async Task<AccountFull> SignInClientSide(
        this IWebClientTester tester,
        AccountFull account,
        CancellationToken cancellationToken = default)
    {
        var session = tester.Session;
        var services = tester.AppServices;
        var sessionsBackend = services.GetRequiredService<ISessionsBackend>();
        var clientAccounts = tester.ClientServices.GetRequiredService<IAccounts>();
        var commander = services.Commander();

        var userIdentity = account.Identities.IsEmpty
            ? new UserIdentity("test", Ulid.NewUlid().ToString()!)
            : account.Identities.Keys.First();
        var newIdentities = account.Identities.IsEmpty
            ? new ApiMap<UserIdentity, string>()
            : account.Identities.Where(x => x.Key != userIdentity).ToApiMap(x => x.Key, x => x.Value);

        // Sign out first if already signed in
        var sessionInfo = await sessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo?.UserId is not null) {
            var signOutCommand = new AccountsBackend_SignOut(session);
            await commander.Call(signOutCommand, cancellationToken).ConfigureAwait(false);
            await WaitForSignOut(clientAccounts, session, cancellationToken).ConfigureAwait(false);
        }

        var command = new AccountsBackend_SignIn(session, userIdentity, newIdentities, account.Claims, AutoCreate: true);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await WaitForSignIn(clientAccounts, session, userIdentity, cancellationToken).ConfigureAwait(false);
    }

    // Reads SessionTemporals[PendingRegistrationKey] for the given session and,
    // if present, calls Accounts_ConfirmRegister to commit the registration.
    // Returns true when a prompt was found and confirmed.
    public static async Task<bool> ConfirmPendingRegistration(
        this AppHost appHost,
        Session session,
        CancellationToken cancellationToken = default)
    {
        var services = appHost.Services;
        var sessionTemporals = services.GetRequiredService<ISessionTemporalsBackend>();
        var json = await sessionTemporals
            .Get(session, Constants.SessionTemporals.PendingRegistrationKey, cancellationToken)
            .ConfigureAwait(false);
        var info = PendingRegistrationInfo.TryParseJson(json);
        if (info is null)
            return false;

        await services.Commander()
            .Call(new Accounts_ConfirmRegister(session, info.Token), true, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public static Task SignOut(
        this IWebTester tester,
        bool deactivate = false,
        CancellationToken cancellationToken = default)
        => tester.AppHost.SignOut(tester.Session, deactivate, cancellationToken);

    public static async Task SignOut(
        this AppHost appHost,
        Session session,
        bool deactivate = false,
        CancellationToken cancellationToken = default)
    {
        var services = appHost.Services;
        var commander = services.Commander();

        var command = new Accounts_SignOut(session, deactivate);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
        var accounts = appHost.Services.GetRequiredService<IAccounts>();
        await WaitForSignOut(accounts, session, cancellationToken).ConfigureAwait(false);
        // await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
    }

    public static async Task ClientSignInWithGoogle(
        this IPage page,
        string user,
        string password)
        => await ClientSignInWith(GoogleDefaults.AuthenticationScheme, user, password, page);

    // Private methods

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

    private static async Task<AccountFull> WaitForSignIn(
        IAccounts accounts,
        Session session,
        UserIdentity userIdentity,
        CancellationToken cancellationToken)
    {
        var cAccount = await Computed
            .Capture(() => accounts.GetOwn(session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull() && x.Identities.ContainsKey(userIdentity), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        return cAccount.Value;
    }

    private static Task WaitForSignOut(
        IAccounts accounts,
        Session session,
        CancellationToken cancellationToken)
        => ComputedTest.When(
            async ct => {
                try {
                    var account = await accounts.GetOwn(session, ct);
                    account.IsGuest.Should().BeTrue();
                }
                catch (InvalidOperationException) {
                    // "Inactive session or GuestId is not set" — session is effectively signed out
                }
            },
            TimeSpan.FromSeconds(10)
        ).WaitAsync(cancellationToken);
}
