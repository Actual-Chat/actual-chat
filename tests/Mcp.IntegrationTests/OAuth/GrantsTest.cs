using ActualChat.OAuth;
using ActualChat.Testing.Host;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public sealed class GrantsTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private IOAuthGrants Grants => Tester.AppServices.GetRequiredService<IOAuthGrants>();
    private IAccountsBackend AccountsBackend => Tester.AppServices.GetRequiredService<IAccountsBackend>();
    private ISessionsBackend SessionsBackend => Tester.AppServices.GetRequiredService<ISessionsBackend>();

    [Fact]
    public async Task ApproveShouldCreateGrantAndBackingSession()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");

        // act
        var authorizationId = await Approve(clientId, "mcp", "offline_access");
        var grants = await ComputedTest.When(async ct => {
            var list = await Grants.List(Tester.Session, ct);
            list.Should().ContainSingle(g => g.Id == authorizationId, because: "consent must show up as a grant");
            return list;
        }, TimeSpan.FromSeconds(10));

        // assert
        var grant = grants.Single(g => g.Id == authorizationId);
        grant.ClientId.Should().Be(clientId);
        grant.ClientName.Should().Be("Test Client");
        grant.Scopes.Should().BeEquivalentTo(["mcp", "offline_access"]);
        grant.ExpiresAt.Should().BeGreaterThan(grant.CreatedAt,
            because: "the grant lives as long as its backing session");
        var backingSession = (await ListOAuthSessions(alice.Id)).Should()
            .ContainSingle(because: "consent creates exactly one backing session").Which;
        var backingSessionInfo = await SessionsBackend.Get(backingSession, default);
        backingSessionInfo.Should().NotBeNull();
        backingSessionInfo!.UserId.Should().Be(alice.Id);
        backingSessionInfo.IsActive.Should().BeTrue();
        var apiKeys = await Tester.AppServices.GetRequiredService<IAccounts>()
            .ListOwnSessions(Tester.Session, SessionKind.ApiKey, default);
        apiKeys.Should().BeEmpty(because: "OAuth sessions must not show up as API keys");
    }

    [Fact]
    public async Task ApproveShouldBeIdempotent()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var command = new OAuthGrants_Approve { Session = Tester.Session, ClientId = clientId, Scopes = ["mcp"] };

        // act
        var firstId = await Tester.Commander.Call(command);
        var secondId = await Tester.Commander.Call(command with { Uuid = ApiCommand.NewUuid() });

        // assert
        secondId.Should().Be(firstId, because: "a repeated consent must reuse the existing authorization");
        (await ListOAuthSessions(alice.Id)).Should()
            .ContainSingle(because: "a repeated consent must reuse the existing backing session");
    }

    [Fact]
    public async Task ConcurrentApprovesShouldConvergeToOneGrant()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");

        // act
        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Approve(clientId, "mcp")));

        // assert
        ids.Distinct().Should().ContainSingle(because: "every concurrent consent must land on the same grant");
        var oauthSessions = await ListOAuthSessions(alice.Id);
        var activeSessions = new List<Session>();
        foreach (var session in oauthSessions)
            if ((await SessionsBackend.Get(session, default))?.IsActive == true)
                activeSessions.Add(session);
        activeSessions.Should().ContainSingle(because: "losing consents must give up their backing sessions");
        await ComputedTest.When(async ct => {
            var grants = await Grants.List(Tester.Session, ct);
            grants.Where(g => g.ClientId == clientId).Should().ContainSingle(because: "duplicates are revoked")
                .Which.Id.Should().Be(ids[0]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ApproveShouldReplaceGrantWhoseSessionIsDead()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var oldId = await Approve(clientId, "mcp");
        var oldSession = (await ListOAuthSessions(alice.Id)).Single();
        await Tester.Commander.Call(new AccountsBackend_SignOut(oldSession, Deactivate: true), true);

        // act
        var newId = await Approve(clientId, "mcp");

        // assert
        newId.Should().NotBe(oldId, because: "a grant without a live session can't issue tokens, so it's replaced");
        var newSession = (await ListOAuthSessions(alice.Id)).Should()
            .ContainSingle(because: "the dead session is gone and the new grant has its own").Which;
        newSession.Should().NotBe(oldSession);
        await ComputedTest.When(async ct => {
            var grants = await Grants.List(Tester.Session, ct);
            grants.Select(g => g.Id).Should().Equal([newId], because: "the replaced grant is revoked");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ApproveShouldRejectGuests()
    {
        // arrange
        var clientId = await RegisterClient("https://c.example/cb");

        // act
        var approve = () => Approve(clientId, "mcp");

        // assert
        await approve.Should().ThrowAsync<Exception>(because: "a guest has no account to grant access to");
    }

    [Fact]
    public async Task ApproveShouldRequireMcpScope()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");

        // act
        var approve = () => Approve(clientId, "offline_access");

        // assert
        await approve.Should().ThrowAsync<Exception>(because: "a grant without the mcp scope is useless");
    }

    [Fact]
    public async Task RevokeShouldDeactivateSessionAndHideGrant()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var authorizationId = await Approve(clientId, "mcp");
        var backingSession = (await ListOAuthSessions(alice.Id)).Single();

        // act
        await Tester.Commander.Call(new OAuthGrants_Revoke {
            Session = Tester.Session,
            AuthorizationId = authorizationId,
        });

        // assert
        await ComputedTest.When(async ct => {
            var grants = await Grants.List(Tester.Session, ct);
            grants.Should().NotContain(g => g.Id == authorizationId, because: "a revoked grant is gone");
        }, TimeSpan.FromSeconds(10));
        var backingSessionInfo = await SessionsBackend.Get(backingSession, default);
        backingSessionInfo.Should().NotBeNull();
        backingSessionInfo!.IsActive.Should().BeFalse(because: "revoking a grant must kill its backing session");
    }

    [Fact]
    public async Task RevokeShouldRevokeTokensOfTheGrant()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var authorizationId = await Approve(clientId, "mcp");
        var tokenIds = new List<string>();
        using (var scope = Tester.AppServices.CreateScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            for (var i = 0; i < 2; i++) {
                var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor {
                    AuthorizationId = authorizationId,
                    Subject = alice.Id.Value,
                    Status = Statuses.Valid,
                    Type = TokenTypeHints.RefreshToken,
                    ReferenceId = $"ref-{authorizationId}-{i}",
                });
                tokenIds.Add((await tokens.GetIdAsync(token))!);
            }
        }

        // act
        await Tester.Commander.Call(new OAuthGrants_Revoke {
            Session = Tester.Session,
            AuthorizationId = authorizationId,
        });

        // assert
        using var assertScope = Tester.AppServices.CreateScope();
        var tokenManager = assertScope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        foreach (var tokenId in tokenIds) {
            var token = await tokenManager.FindByIdAsync(tokenId);
            token.Should().NotBeNull();
            (await tokenManager.GetStatusAsync(token!)).Should().Be(Statuses.Revoked,
                because: "every token issued under a revoked grant must die with it");
        }
    }

    [Fact]
    public async Task RevokeShouldNotTouchOtherUsersGrants()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var authorizationId = await Approve(clientId, "mcp");
        await using var bobTester = AppHost.NewWebClientTester(Out);
        await bobTester.SignInAsUniqueBob();

        // act
        var revoke = () => bobTester.Commander.Call(new OAuthGrants_Revoke {
            Session = bobTester.Session,
            AuthorizationId = authorizationId,
        }, CancellationToken.None);

        // assert
        await revoke.Should().ThrowAsync<Exception>(because: "another user's grant must look like it doesn't exist");
        var grants = await Grants.List(Tester.Session, default);
        grants.Should().ContainSingle(g => g.Id == authorizationId, because: "Alice's grant must stay intact");
    }

    [Fact]
    public async Task GetClientShouldDescribeRegisteredClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("http://localhost/cb", "https://c.example/cb");

        // act
        var info = await Grants.GetClient(Tester.Session, clientId, default);

        // assert
        info.Should().NotBeNull();
        info!.ClientId.Should().Be(clientId);
        info.ClientName.Should().Be("Test Client");
        info.IsLoopback.Should().BeTrue(because: "one of the redirect URIs is http://localhost");
        info.RedirectHosts.Should().BeEquivalentTo(["localhost", "c.example"]);
        info.Scopes.Should().BeEquivalentTo(["mcp", "offline_access"]);
    }

    [Fact]
    public async Task GetClientShouldReturnNullForUnknownClient()
    {
        // act
        var info = await Grants.GetClient(Tester.Session, "no-such-client", default);

        // assert
        info.Should().BeNull(because: "an unregistered client id has no description");
    }

    // Private methods

    private async Task<List<Session>> ListOAuthSessions(UserId userId)
    {
        var sessions = await AccountsBackend.ListSessions(userId, default);
        return sessions.Where(s => s.Kind == SessionKind.OAuth).ToList();
    }

    private Task<string> Approve(string clientId, params string[] scopes)
        => Tester.Commander.Call(new OAuthGrants_Approve {
            Session = Tester.Session,
            ClientId = clientId,
            Scopes = scopes.ToApiArray(),
        });
}
