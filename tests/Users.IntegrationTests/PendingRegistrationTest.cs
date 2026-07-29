using System.Security.Claims;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.IntegrationTests;

/// <summary>
/// API-level tests for the sign-in confirmation flow: when no account exists,
/// the server stashes a SecureToken in SessionTemporals[PendingRegistrationKey]
/// instead of auto-creating; Accounts.OnConfirmRegister/OnCancelRegister
/// commit or discard the registration.
/// </summary>
[Collection(nameof(UserCollection))]
public class PendingRegistrationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();
    private AuthHelper AuthHelper => AppHost.Services.GetRequiredService<AuthHelper>();
    private DbHub<UsersDbContext> DbHub => AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
    private ISessionTemporalsBackend SessionTemporals => AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();

    [Fact]
    public async Task DoesNotLinkUnverifiedEmail()
    {
        // arrange
        var email = UniqueNames.Email("unverified-link");
        _ = await RegisterEmailAccount(email);
        var session = await NewSession();
        var providerIdentity = new UserIdentity(AuthSchema.Google, UniqueNames.Random());
        var identity = new ClaimsIdentity(AuthSchema.Google);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, providerIdentity.Value));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));

        // act
        await AuthHelper.SignIn(session, new ClaimsPrincipal(identity), CancellationToken.None);

        // assert
        var ownerId = await GetIdentityOwner(providerIdentity);
        ownerId.Should().BeNull();
    }

    [Fact]
    public async Task LinksVerifiedEmail()
    {
        // arrange
        var email = UniqueNames.Email("verified-link");
        var existingAccount = await RegisterEmailAccount(email);
        var session = await NewSession();
        var providerIdentity = new UserIdentity(AuthSchema.Google, UniqueNames.Random());
        var identity = new ClaimsIdentity(AuthSchema.Google);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, providerIdentity.Value));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        identity.AddClaim(new Claim(AuthSchema.EmailVerifiedClaim, bool.TrueString));

        // act
        await AuthHelper.SignIn(session, new ClaimsPrincipal(identity), CancellationToken.None);

        // assert
        var ownerId = await GetIdentityOwner(providerIdentity);
        ownerId.Should().Be(existingAccount.Id);
    }

    [Fact]
    public async Task DoesNotAddUnverifiedEmailIdentity()
    {
        // arrange
        var providerIdentity = new UserIdentity(AuthSchema.Google, UniqueNames.Random());
        var initialSession = await NewSession();
        await SignInExternal(initialSession, providerIdentity, null, false);
        var session = await NewSession();
        var email = UniqueNames.Email("unverified-identity");

        // act
        await SignInExternal(session, providerIdentity, email, false);

        // assert
        var emailIdentity = UserIdentityExt.NewEmailIdentity(ActualChat.Email.Parse(email));
        var ownerId = await GetIdentityOwner(emailIdentity);
        ownerId.Should().BeNull();
    }

    [Fact]
    public async Task AddsVerifiedEmailIdentity()
    {
        // arrange
        var providerIdentity = new UserIdentity(AuthSchema.Google, UniqueNames.Random());
        var initialSession = await NewSession();
        await SignInExternal(initialSession, providerIdentity, null, false);
        var session = await NewSession();
        var email = UniqueNames.Email("verified-identity");

        // act
        await SignInExternal(session, providerIdentity, email, true);

        // assert
        var emailIdentity = UserIdentityExt.NewEmailIdentity(ActualChat.Email.Parse(email));
        var ownerId = await GetIdentityOwner(emailIdentity);
        ownerId.Should().NotBeNull();
    }

    [Fact]
    public async Task SignInWithNoAccountStashesPendingRegistration()
    {
        // arrange
        var session = await NewSession();
        var email = UniqueNames.Email("pending-stash");

        // act
        await SignIn(session, email);

        // assert
        var account = await Accounts.GetOwn(session, default);
        account.IsGuest.Should().BeTrue("no account should be created until the user confirms");

        var info = await GetPendingRegistration(session);
        info.Should().NotBeNull("a PendingRegistration descriptor must be stashed");
        info!.Provider.Should().Be("Email");
        info.Identifier.Should().Be(email);
        info.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConfirmRegisterCreatesAccountAndSignsIn()
    {
        // arrange
        var session = await NewSession();
        var email = UniqueNames.Email("pending-confirm");
        await SignIn(session, email);
        var info = await GetPendingRegistration(session);
        info.Should().NotBeNull();

        // act
        await Commander.Call(new Accounts_ConfirmRegister(session, info!.Token));

        // assert
        var account = await Accounts.GetOwn(session, default);
        account.IsGuest.Should().BeFalse("ConfirmRegister must create the account");
        account.Identities.GetEmails().Should().Contain(email);
        (await GetPendingRegistration(session))
            .Should().BeNull("ConfirmRegister must clear the prompt");
    }

    [Fact]
    public async Task CancelRegisterClearsPromptAndWritesError()
    {
        // arrange
        var session = await NewSession();
        var email = UniqueNames.Email("pending-cancel");
        await SignIn(session, email);
        var info = await GetPendingRegistration(session);
        info.Should().NotBeNull();

        // act
        await Commander.Call(new Accounts_CancelRegister(session, info!.Token));

        // assert
        (await GetPendingRegistration(session))
            .Should().BeNull("CancelRegister must clear the prompt");
        var signInError = await SessionTemporals.Get(
            session, Constants.SessionTemporals.SignInErrorKey, CancellationToken.None);
        signInError.Should().Be(Constants.SessionTemporals.SignInCanceledMessage);
        var account = await Accounts.GetOwn(session, default);
        account.IsGuest.Should().BeTrue("no account should be created");
    }

    [Fact]
    public async Task ConfirmRegisterRejectsTokenFromDifferentSession()
    {
        // arrange — session A produces a token; session B tries to use it
        var sessionA = await NewSession();
        var sessionB = await NewSession();
        var email = UniqueNames.Email("pending-cross-session");
        await SignIn(sessionA, email);
        var info = await GetPendingRegistration(sessionA);
        info.Should().NotBeNull();

        // act + assert
        var act = () => Commander.Call(new Accounts_ConfirmRegister(sessionB, info!.Token));
        await act.Should().ThrowAsync<Exception>(
            "a PendingRegistration token issued for one session must not be redeemable from another");

        // sessionB still has no account, no pending registration
        var accountB = await Accounts.GetOwn(sessionB, default);
        accountB.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task CancelRegisterRejectsTokenFromDifferentSession()
    {
        // arrange — session A produces a token; session B tries to cancel with it
        var sessionA = await NewSession();
        var sessionB = await NewSession();
        var email = UniqueNames.Email("pending-cancel-cross-session");
        await SignIn(sessionA, email);
        var info = await GetPendingRegistration(sessionA);
        info.Should().NotBeNull();

        // act
        var act = () => Commander.Call(new Accounts_CancelRegister(sessionB, info!.Token));

        // assert
        await act.Should().ThrowAsync<Exception>(
            "a PendingRegistration token issued for one session must not be usable from another");
        (await GetPendingRegistration(sessionA))
            .Should().NotBeNull("session A's prompt must survive a cancel attempted from another session");
        var signInErrorB = await SessionTemporals.Get(
            sessionB, Constants.SessionTemporals.SignInErrorKey, CancellationToken.None);
        signInErrorB.Should().BeNull();
    }

    [Fact]
    public async Task SignInForExistingAccountDoesNotStash()
    {
        // arrange — register one user via the new flow
        var sessionFirst = await NewSession();
        var email = UniqueNames.Email("existing-account");
        await SignIn(sessionFirst, email);
        var info = await GetPendingRegistration(sessionFirst);
        await Commander.Call(new Accounts_ConfirmRegister(sessionFirst, info!.Token));
        var firstAccount = await Accounts.GetOwn(sessionFirst, default);
        firstAccount.IsGuest.Should().BeFalse();

        // act — second sign-in with the same email from a fresh session
        var sessionSecond = await NewSession();
        await SignIn(sessionSecond, email);

        // assert — direct sign-in, no prompt
        (await GetPendingRegistration(sessionSecond))
            .Should().BeNull("existing accounts should sign in directly without a confirmation prompt");
        var secondAccount = await Accounts.GetOwn(sessionSecond, default);
        secondAccount.IsGuest.Should().BeFalse();
        secondAccount.Id.Should().Be(firstAccount.Id);
    }

    [Fact]
    public async Task SuccessfulSignInClearsStaleStash()
    {
        // arrange — first attempt against an unknown email leaves a prompt
        var session = await NewSession();
        var unknownEmail = UniqueNames.Email("stale-prompt-unknown");
        await SignIn(session, unknownEmail);
        (await GetPendingRegistration(session)).Should().NotBeNull();

        // and a separate existing account already lives in the system
        var seedSession = await NewSession();
        var existingEmail = UniqueNames.Email("stale-prompt-existing");
        await SignIn(seedSession, existingEmail);
        var seedInfo = await GetPendingRegistration(seedSession);
        await Commander.Call(new Accounts_ConfirmRegister(seedSession, seedInfo!.Token));

        // act — same session signs in successfully against the existing email
        await SignIn(session, existingEmail);

        // assert — the stale prompt is gone after a successful sign-in
        (await GetPendingRegistration(session))
            .Should().BeNull("a successful sign-in must clear any leftover PendingRegistration prompt");
        var account = await Accounts.GetOwn(session, default);
        account.IsGuest.Should().BeFalse();
    }

    // Helpers

    private async Task<Session> NewSession()
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));
        return session;
    }

    private async Task<AccountFull> RegisterEmailAccount(string email)
    {
        var session = await NewSession();
        await SignIn(session, email);
        var info = await GetPendingRegistration(session);
        await Commander.Call(new Accounts_ConfirmRegister(session, info!.Token));
        return await Accounts.GetOwn(session, CancellationToken.None);
    }

    private async Task<UserId?> GetIdentityOwner(UserIdentity identity)
    {
        var dbContext = await DbHub.CreateDbContext(CancellationToken.None);
        await using var _ = dbContext;
        return await dbContext.GetUserIdByIdentity(identity, false, CancellationToken.None);
    }

    private async Task SignInExternal(
        Session session,
        UserIdentity identity,
        string? email,
        bool isVerified)
    {
        var claims = new ApiMap<string, string>();
        if (email is not null)
            claims = claims.With(ClaimTypes.Email, email);
        if (isVerified)
            claims = claims.With(AuthSchema.EmailVerifiedClaim, bool.TrueString);
        var command = new AccountsBackend_SignIn(
            session,
            identity,
            new ApiMap<UserIdentity, string>(),
            claims,
            AutoCreate: true);
        await Commander.Call(command);
    }

    private Task SignIn(Session session, string email)
    {
        var emailIdentity = UserIdentityExt.NewEmailIdentity(ActualChat.Email.Parse(email));
        var identities = new ApiMap<UserIdentity, string>().With(emailIdentity, "");
        var claims = new ApiMap<string, string>().With(ClaimTypes.Email, email);
        return Commander.Call(new AccountsBackend_SignIn(session, emailIdentity, identities, claims));
    }

    private async Task<PendingRegistrationInfo?> GetPendingRegistration(Session session)
    {
        var json = await SessionTemporals.Get(
            session, Constants.SessionTemporals.PendingRegistrationKey, CancellationToken.None);
        return PendingRegistrationInfo.TryParseJson(json);
    }
}
