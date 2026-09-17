using System.Net;
using ActualChat.OAuth;
using ActualChat.Testing.Host;
using ModelContextProtocol.Protocol;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public class McpAccessTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task JwtShouldListToolsAndPostAsUser()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: "OAuth chat");
        var (_, tokens) = await ConnectClient();

        // act
        var client = await CreateMcpClient(tokens.GetProperty("access_token").GetString()!);
        var tools = await client.ListToolsAsync();
        var result = await client.CallToolAsync("post_message",
            new Dictionary<string, object?> { ["chatId"] = chatId.Value, ["text"] = "via oauth" });

        // assert
        tools.Select(t => t.Name).Should().Contain("post_message");
        result.IsError.Should().NotBe(true);
        var lid = GetPostedLid(result);
        var entry = await Tester.Chats.GetEntry(Tester.Session, ChatEntryId.New(chatId, lid));
        entry.Should().NotBeNull();
        entry!.Content.Should().Be("via oauth");
        entry.IsViaApi.Should().BeTrue();
        var author = await Tester.Authors.GetAccount(Tester.Session, chatId, entry.AuthorId, default);
        author!.Id.Should().Be(alice.Id);
    }

    [Fact]
    public async Task ApiKeyShouldStillWork()
    {
        await Tester.SignInAsUniqueAlice();
        var apiKey = await Tester.Commander.Call(new Accounts_CreateApiKey { Session = Tester.Session, Name = "k" });

        // act
        var client = await CreateMcpClient(apiKey);

        // assert
        (await client.ListToolsAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExpiredJwtShouldBeInvalidToken()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (_, tokens) = await ConnectClient();
        await Task.Delay(TimeSpan.FromSeconds(4));

        // act
        var response = await SendInitialize("Bearer " + tokens.GetProperty("access_token").GetString());

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
    }

    [Fact]
    public async Task RevokedGrantShouldRejectJwtAndRefresh()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var grants = Tester.AppServices.GetRequiredService<IOAuthGrants>();
        var grant = (await grants.List(Tester.Session, default)).Single(g => g.ClientId == clientId);

        // act
        await Tester.Commander.Call(new OAuthGrants_Revoke { Session = Tester.Session, AuthorizationId = grant.Id });

        // assert
        await ComputedTest.When(async _ => {
            var response = await SendInitialize("Bearer " + accessToken);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }, TimeSpan.FromSeconds(10));
        var (status, body) = await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task DeactivatedBackingSessionShouldRejectJwt()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (_, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var sid = ReadJwt(accessToken).Claims.Single(c => c.Type == "sid").Value;

        // act
        await Tester.Commander.Call(new Accounts_DeactivateSession {
            Session = Tester.Session,
            IdPrefix = sid[..CoreConstants.Session.IdPrefixLength],
        });

        // assert
        await ComputedTest.When(async _ => {
            var response = await SendInitialize("Bearer " + accessToken);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task JwtForAnotherAudienceShouldBeRejected()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var pkce = NewPkce();

        // act
        var authorizeResponse = await Authorize(clientId, "https://c.example/cb", pkce,
            resource: "https://other.example/api");
        var authorizeBody = await authorizeResponse.Content.ReadAsStringAsync();

        // assert
        // OpenIddict only recognizes the MCP URL as a registered resource (Task 6), so it rejects the
        // request at the authorize step itself, before a code (and hence a token) can ever be minted.
        authorizeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        authorizeBody.Should().Contain("error:invalid_target");
    }

    // Private methods

    private static long GetPostedLid(CallToolResult result)
    {
        var json = result.StructuredContent!.Value;
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("result", out var inner)
            ? inner.GetInt64()
            : json.GetInt64();
    }
}
