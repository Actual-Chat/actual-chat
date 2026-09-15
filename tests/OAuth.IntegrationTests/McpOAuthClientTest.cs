using System.Net;
using ActualChat.Mcp;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ActualChat.OAuth.IntegrationTests;

/// <summary>
/// Drives the official MCP SDK's OAuth client against the server end to end: discovery, consent
/// (with the redirect delegate playing the browser), token exchange, tool calls, and refresh.
/// The client is pre-registered over raw HTTP: the SDK's own DCR request asks for
/// <c>client_secret_post</c>, which <c>/oauth/register</c> rejects.
/// </summary>
[Collection(nameof(OAuthCollection))]
public class McpOAuthClientTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private static readonly Uri RedirectUri = new("http://localhost/callback");

    private int _consentCount;

    [Fact(Skip = "Blocked: the SDK requests the challenge's scope=\"mcp\", so no refresh token is issued")]
    public async Task SdkClientShouldConsentCallAndRefresh()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: "SDK chat");
        var clientId = await RegisterClient(RedirectUri.ToString());
        var tokenCache = new RecordingTokenCache();

        // act
        await using var client = await ConnectSdkClient(Tester, clientId, tokenCache);
        var tools = await client.ListToolsAsync();
        var post = await client.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = "hello from sdk",
        });
        await Task.Delay(TimeSpan.FromSeconds(4));
        var list = await client.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["limit"] = 10,
        });

        // assert
        tools.Select(t => t.Name).Should().Contain("post_message");
        post.IsError.Should().NotBe(true);
        list.IsError.Should().NotBe(true,
            because: "the SDK must have refreshed the expired access token transparently");
        DeserializeResult<McpListMessagesResult>(list).Messages.Select(m => m.Text)
            .Should().Contain("hello from sdk");
        _consentCount.Should().Be(1, because: "an expired access token is refreshed, not re-authorized");
        tokenCache.Stored.Should().HaveCount(2,
            because: "the first token set comes from the code exchange, the second from the refresh");
        tokenCache.Stored[1].AccessToken.Should().NotBe(tokenCache.Stored[0].AccessToken);
        tokenCache.Stored[1].RefreshToken.Should().NotBe(tokenCache.Stored[0].RefreshToken,
            because: "refresh tokens rotate on every use");
    }

    [Fact(Skip = "Blocked: the SDK requests the challenge's scope=\"mcp\", so no refresh token is issued")]
    public async Task SdkClientShouldRefreshOnInvalidTokenChallenge()
    {
        // The SDK refreshes proactively when it knows the expiry; hiding it makes the SDK present the
        // expired token, so the server's 401 challenge is what has to drive the refresh and the retry.
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri.ToString());
        var tokenCache = new RecordingTokenCache(hidesExpiry: true);
        await using var client = await ConnectSdkClient(Tester, clientId, tokenCache);
        await Task.Delay(TimeSpan.FromSeconds(4));

        // act
        var tools = await client.ListToolsAsync();

        // assert
        tools.Should().NotBeEmpty();
        _consentCount.Should().Be(1,
            because: "the 401 invalid_token challenge must be answered with a refresh, not a new consent");
        tokenCache.Stored.Should().HaveCount(2, because: "the refresh stores a second token set");
        tokenCache.Stored[1].AccessToken.Should().NotBe(tokenCache.Stored[0].AccessToken);
    }

    [Fact]
    public async Task TwoUsersShouldGetIsolatedGrants()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (aliceChatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Alice private");
        await using var bobTester = AppHost.NewWebClientTester(Out);
        await bobTester.SignInAsUniqueBob();
        var (bobChatId, _) = await bobTester.CreateChat(isPublicChat: false, title: "Bob private");
        var clientId = await RegisterClient(RedirectUri.ToString());

        // act
        // Both use the same client_id, as one installed app would for two accounts
        await using var aliceClient = await ConnectSdkClient(Tester, clientId, new RecordingTokenCache());
        await using var bobClient = await ConnectSdkClient(bobTester, clientId, new RecordingTokenCache());
        var aliceChats = DeserializeResult<McpListChatsResult>(await ListGroupChats(aliceClient));
        var bobChats = DeserializeResult<McpListChatsResult>(await ListGroupChats(bobClient));
        var bobReadsAlice = await bobClient.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = aliceChatId.Value,
        });

        // assert
        aliceChats.Chats.Select(c => c.Id).Should().Contain(aliceChatId.Value);
        bobChats.Chats.Select(c => c.Id).Should().Contain(bobChatId.Value);
        bobChats.Chats.Select(c => c.Id).Should().NotContain(aliceChatId.Value,
            because: "Bob's grant maps to Bob's session, not Alice's");
        bobReadsAlice.IsError.Should().Be(true, because: "Bob has no access to Alice's private chat");
    }

    // Private methods

    private async Task<McpClient> ConnectSdkClient(WebClientTester tester, string clientId, ITokenCache tokenCache)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri(BaseUri, OAuthConstants.McpResourcePath),
            OAuth = new ClientOAuthOptions {
                RedirectUri = RedirectUri,
                ClientId = clientId,
                Scopes = ["mcp", "offline_access"],
                AuthorizationRedirectDelegate = (uri, _, ct) => DriveConsent(tester, uri, ct),
                TokenCache = tokenCache,
            },
        });
        return await McpClient.CreateAsync(transport);
    }

    private async Task<string?> DriveConsent(WebClientTester tester, Uri authorizationUri, CancellationToken ct)
    {
        // Plays the browser: GET the authorize URL with the user's cookie, approve via the command, return the code
        _consentCount++;
        var relative = authorizationUri.PathAndQuery;
        var response = await SendAs(tester.Session, HttpMethod.Get, relative);
        if (response.Headers.Location?.ToString().Contains(OAuthConstants.ConsentPath) == true) {
            var query = QueryHelpers.ParseQuery(authorizationUri.Query);
            await tester.Commander.Call(new OAuthGrants_Approve {
                Session = tester.Session,
                ClientId = query["client_id"].ToString(),
                Scopes = query["scope"].ToString().Split(' ').ToApiArray(),
            }, ct);
            response = await SendAs(tester.Session, HttpMethod.Get, relative);
        }
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        return GetQueryValue(response.Headers.Location!, "code");
    }

    private static ValueTask<CallToolResult> ListGroupChats(McpClient client)
        => client.CallToolAsync("list_group_chats", new Dictionary<string, object?> { ["limit"] = 100 });

    // Nested types

    private sealed class RecordingTokenCache(bool hidesExpiry = false) : ITokenCache
    {
        private TokenContainer? _current;

        public List<TokenContainer> Stored { get; } = [];

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            Stored.Add(tokens);
            _current = hidesExpiry
                ? new TokenContainer {
                    TokenType = tokens.TokenType,
                    AccessToken = tokens.AccessToken,
                    RefreshToken = tokens.RefreshToken,
                    Scope = tokens.Scope,
                    ObtainedAt = tokens.ObtainedAt,
                }
                : tokens;
            return default;
        }

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
            => new(_current);
    }
}
