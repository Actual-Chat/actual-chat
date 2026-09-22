using ActualChat.Testing.Host;
using ActualChat.Testing.Passkeys;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class PasskeyAuthTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string RpId = "localhost";
    private const string Origin = "https://localhost";

    private IPasskeyAuth PasskeyAuth => AppHost.Services.GetRequiredService<IPasskeyAuth>();
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();
    private IPasskeysBackend PasskeysBackend => AppHost.Services.GetRequiredService<IPasskeysBackend>();

    [Fact]
    public async Task IsEnabledShouldBeTrueOutsideProduction()
        => (await PasskeyAuth.IsEnabled(default)).Should().BeTrue();

    [Fact]
    public async Task GetRpIdShouldComeFromSettings()
        => (await PasskeyAuth.GetRpId(default)).Should().Be(RpId);

    [Fact]
    public async Task RegisterThenSignInShouldLandOnTheSameAccount()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);

        // act
        var passkey = await Register(tester.Session, authenticator);
        var signInSession = await NewSession();
        var isSignedIn = await SignIn(signInSession, authenticator);

        // assert
        isSignedIn.Should().BeTrue();
        passkey.Id.Should().Be(authenticator.CredentialId);
        passkey.IsSynced.Should().BeTrue();
        var signedIn = await Accounts.GetOwn(signInSession, default);
        signedIn.Id.Should().Be(account.Id);
        var listed = await PasskeyAuth.ListOwn(tester.Session, default);
        listed.Should().ContainSingle(x => x.Id == passkey.Id && x.LastUsedAt != null);
    }

    [Fact]
    public async Task BeginRegistrationShouldRefuseGuests()
    {
        // arrange
        var session = await NewSession();

        // act
        var act = () => Commander.Call(new PasskeyAuth_BeginRegistration { Session = session });

        // assert
        await act.Should().ThrowAsync<GuestAccountException>("only a signed-in user can add a passkey");
    }

    [Fact]
    public async Task BeginRegistrationShouldRefuseSignUpPurposeForNow()
    {
        // arrange
        var session = await NewSession();

        // act
        var act = () => Commander.Call(
            new PasskeyAuth_BeginRegistration { Session = session, Purpose = PasskeyPurpose.SignUp });

        // assert
        await act.Should().ThrowAsync<NotSupportedException>("passkey-first signup ships in a later PR")
            .WithMessage("*isn't available yet*");
    }

    [Fact]
    public async Task BeginRegistrationShouldRefuseOverlongName()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        var name = new string('x', 65);

        // act
        var act = () => Commander.Call(new PasskeyAuth_BeginRegistration { Session = tester.Session, Name = name });

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>("the name is bounded at Begin, not only at Rename")
            .WithMessage("*1 to 64 characters*");
    }

    [Fact]
    public async Task ChallengeShouldBeSingleUse()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var options = await Commander.Call(new PasskeyAuth_BeginRegistration { Session = tester.Session });
        var attestation = authenticator.CreateAttestationJson(options);
        await Commander.Call(
            new PasskeyAuth_CompleteRegistration { Session = tester.Session, AttestationJson = attestation });

        // act
        var act = () => Commander.Call(
            new PasskeyAuth_CompleteRegistration { Session = tester.Session, AttestationJson = attestation });

        // assert
        await act.Should().ThrowAsync<Exception>("the challenge is consumed by the first completion")
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task CompleteRegistrationShouldRefuseAnotherAccountOnTheSameSession()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var alice = await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var options = await Commander.Call(new PasskeyAuth_BeginRegistration { Session = tester.Session });
        var attestation = authenticator.CreateAttestationJson(options);
        var bob = await tester.SignInAsUniqueBob(); // Sign-out + sign-in keeps the session id
        bob.Id.Should().NotBe(alice.Id);

        // act
        var act = () => Commander.Call(
            new PasskeyAuth_CompleteRegistration { Session = tester.Session, AttestationJson = attestation });

        // assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>("the challenge was issued for Alice, not Bob")
            .WithMessage("*another account*");
        (await PasskeysBackend.List(bob.Id, default)).Should().BeEmpty("Alice's user handle must not land on Bob");
        (await PasskeysBackend.List(alice.Id, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task SignInShouldRejectWrongOrigin()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        await Register(tester.Session, authenticator);
        var session = await NewSession();
        var options = await Commander.Call(new PasskeyAuth_BeginSignIn { Session = session });
        // The helper signs clientDataJSON as-is, so a foreign origin is a real, well-signed assertion
        authenticator.Origin = "https://evil.example";

        // act
        var act = () => Commander.Call(new PasskeyAuth_CompleteSignIn {
            Session = session,
            AssertionJson = authenticator.CreateAssertionJson(options),
        });

        // assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>("an origin outside the allow-list must be refused");
        (await Accounts.GetOwn(session, default)).IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task SignInShouldRejectNonMonotonicCounter()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        await Register(tester.Session, authenticator);
        (await SignIn(await NewSession(), authenticator)).Should().BeTrue();
        authenticator.SignCount = 0; // a cloned authenticator replays an old counter
        var replaySession = await NewSession();

        // act
        var act = () => SignIn(replaySession, authenticator);

        // assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>("a counter that doesn't advance signals a cloned key");
    }

    [Fact]
    public async Task SignInShouldRejectZeroCounterAfterNonZeroOne()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);
        (await SignIn(await NewSession(), authenticator)).Should().BeTrue();
        var storedBefore = await TestWait.When(async ct => {
            var stored = await PasskeysBackend.Get(account.Id, passkey.Id, ct);
            stored!.SignCount.Should().Be(authenticator.SignCount, "the first sign-in persists the counter");
            return stored;
        });
        authenticator.FixedSignCount = 0; // Fido2NetLib skips its counter check for a literal zero
        var replaySession = await NewSession();

        // act
        var act = () => SignIn(replaySession, authenticator);

        // assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>("a zero counter after a nonzero one is a clone");
        (await Accounts.GetOwn(replaySession, default)).IsGuest.Should().BeTrue();
        var storedAfter = await PasskeysBackend.Get(account.Id, passkey.Id, default);
        storedAfter!.SignCount.Should()
            .Be(storedBefore!.SignCount, "a rejected assertion must not erase the counter");
    }

    [Fact]
    public async Task SignInWithUnknownCredentialShouldFail()
    {
        // arrange
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var session = await NewSession();
        var options = await Commander.Call(new PasskeyAuth_BeginSignIn { Session = session });

        // act
        var act = () => Commander.Call(new PasskeyAuth_CompleteSignIn {
            Session = session,
            AssertionJson = authenticator.CreateAssertionJson(options),
        });

        // assert
        await act.Should().ThrowAsync<Exception>().WithMessage("*isn't linked to an account*");
    }

    [Fact]
    public async Task SecondPasskeyShouldReuseTheUserHandle()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var first = new SoftwareAuthenticator(RpId, Origin);
        using var second = new SoftwareAuthenticator(RpId, Origin);

        // act
        await Register(tester.Session, first);
        await Register(tester.Session, second);

        // assert
        second.UserHandle.Should().Equal(first.UserHandle, "Apple and Google dedupe passkeys by RP + user.id");
    }

    [Fact]
    public async Task RenameShouldChangeTheName()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);

        // act
        await Commander.Call(
            new PasskeyAuth_Rename { Session = tester.Session, Id = passkey.Id, Name = "Work laptop" });

        // assert
        var listed = await PasskeyAuth.ListOwn(tester.Session, default);
        listed.Single().Name.Should().Be("Work laptop");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x", 65)]
    public async Task RenameShouldRefuseEmptyAndOverlongNames(string namePart, int repeatCount = 1)
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);
        var name = string.Concat(Enumerable.Repeat(namePart, repeatCount));

        // act
        var act = () => Commander.Call(
            new PasskeyAuth_Rename { Session = tester.Session, Id = passkey.Id, Name = name });

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>("a passkey name is 1 to 64 characters after trimming")
            .WithMessage("*1 to 64 characters*");
        (await PasskeyAuth.ListOwn(tester.Session, default)).Single().Name.Should().Be(passkey.Name);
    }

    [Fact]
    public async Task DeleteShouldRemoveThePasskeyWhenAnotherIdentityExists()
    {
        // arrange — SignInAsUniqueAlice creates an account with a non-passkey identity
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);

        // act
        await Commander.Call(new PasskeyAuth_Delete { Session = tester.Session, Id = passkey.Id });

        // assert
        (await PasskeyAuth.ListOwn(tester.Session, default)).Should().BeEmpty();
        (await SignIn(await NewSession(), authenticator)).Should().BeFalse("a deleted passkey can't sign in");
    }

    [Fact]
    public async Task DeleteShouldRefuseTheLastPasskeyWithoutAnotherIdentity()
    {
        // arrange — an account whose only identity is the passkey
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);
        await RemoveNonPasskeyIdentities(tester.Session);

        // act
        var act = () => Commander.Call(new PasskeyAuth_Delete { Session = tester.Session, Id = passkey.Id });

        // assert
        await act.Should().ThrowAsync<Exception>("deleting the only way in would lock the account")
            .WithMessage("*only way to sign in*");
    }

    // Private methods

    private async Task<Passkey> Register(Session session, SoftwareAuthenticator authenticator)
    {
        var options = await Commander.Call(new PasskeyAuth_BeginRegistration { Session = session });
        var attestation = authenticator.CreateAttestationJson(options);
        return await Commander.Call(
            new PasskeyAuth_CompleteRegistration { Session = session, AttestationJson = attestation });
    }

    private async Task<bool> SignIn(Session session, SoftwareAuthenticator authenticator)
    {
        var options = await Commander.Call(new PasskeyAuth_BeginSignIn { Session = session });
        var assertion = authenticator.CreateAssertionJson(options);
        try {
            return await Commander.Call(
                new PasskeyAuth_CompleteSignIn { Session = session, AssertionJson = assertion });
        }
        catch (Exception e) when (e.Message.Contains("isn't linked to an account")) {
            return false;
        }
    }

    private async Task<Session> NewSession()
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));
        return session;
    }

    private async Task RemoveNonPasskeyIdentities(Session session)
    {
        var account = await Accounts.GetOwn(session, default);
        var dbHub = AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
        await using var dbContext = await dbHub.CreateDbContext(true);
        var passkeyIdPrefix = $"{AuthSchema.Passkey}/";
        var rows = dbContext.AccountIdentities
            .Where(x => x.DbAccountId == account.Id.Value && !x.Id.StartsWith(passkeyIdPrefix));
        dbContext.AccountIdentities.RemoveRange(rows);
        await dbContext.SaveChangesAsync();
        var accountsBackend = AppHost.Services.GetRequiredService<IAccountsBackend>();
        // Direct DB edit: Fusion can't see it, and a recompute that started before the edit may land
        // after a single invalidation, so re-invalidate until the cached account reflects the row set
        var deadline = CpuTimestamp.Now + TimeSpan.FromSeconds(5);
        while (true) {
            using (Invalidation.Begin())
                _ = accountsBackend.Get(account.Id, default);
            var current = await Accounts.GetOwn(session, default);
            if (current.Identities.Keys.All(x => x.Schema == AuthSchema.Passkey))
                return;

            CpuTimestamp.Now.Should().BeLessThan(deadline, "the cached account must reflect the removed identities");
            await Task.Delay(50);
        }
    }
}
