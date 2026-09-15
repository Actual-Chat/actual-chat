using System.Net.Http.Headers;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class AuthTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ApiKeySession_IsAccepted()
    {
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var tools = await client.ListToolsAsync();
        tools.Should().NotBeEmpty();
        tools.Select(t => t.Name).Should().Contain("post_message");
    }

    [Fact]
    public async Task NonApiKeySession_IsRejected()
    {
        await Tester.SignInAsUniqueAlice();

        var connect = CreateClientWithRawKey(Tester.Session.Id).AsAsyncFunc();
        await connect.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task MissingHeader_IsRejected()
    {
        var response = await SendInitialize(authorization: null);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().StartWith("Bearer");
    }

    [Fact]
    public async Task NonBearerHeader_IsRejected()
    {
        var response = await SendInitialize(authorization: "Basic dXNlcjpwYXNz");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKeyForGuestSession_IsRejected()
    {
        var unboundKey = await Tester.Commander
            .Call(new SessionsBackend_Upsert(SessionExt.NewApiKey()));
        unboundKey.Session.Kind.Should().Be(SessionKind.ApiKey);

        var connect = CreateClientWithRawKey(unboundKey.Session.Id).AsAsyncFunc();
        await connect.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task NonexistentApiKey_IsRejected()
    {
        // Well-formed API key (passes Session.IsValid and Kind == ApiKey)
        // but no matching row in the sessions table.
        var fakeKey = SessionExt.NewApiKey();
        fakeKey.Kind.Should().Be(SessionKind.ApiKey);

        var connect = CreateClientWithRawKey(fakeKey.Id).AsAsyncFunc();
        await connect.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task MalformedBearerToken_IsRejected()
    {
        var response = await SendInitialize(authorization: "Bearer not-a-valid-session-id");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/api/mcp")]
    public async Task ProtectedResourceDocumentShouldNameMcpEndpoint(string path)
    {
        // arrange
        using var http = Tester.AppHost.NewHttpClient();

        // act
        var doc = JsonDocument.Parse(await http.GetStringAsync(path)).RootElement;

        // assert
        var baseUri = Tester.UrlMapper.BaseUri;
        doc.GetProperty("resource").GetString().Should().Be(new Uri(baseUri, "/api/mcp").ToString());
        doc.GetProperty("authorization_servers")[0].GetString().Should().Be(baseUri.ToString().TrimEnd('/'));
        doc.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("mcp", "offline_access");
        doc.GetProperty("bearer_methods_supported")[0].GetString().Should().Be("header");
    }

    [Fact]
    public async Task MissingHeaderShouldPointAtResourceMetadata()
    {
        // arrange/act
        var response = await SendInitialize(authorization: null);

        // assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        var header = response.Headers.WwwAuthenticate.ToString();
        header.Should().Contain("resource_metadata=\"").And.Contain("/.well-known/oauth-protected-resource/api/mcp\"");
        header.Should().Contain("scope=\"mcp offline_access\"",
            because: "SDK clients request the advertised scope, and only offline_access yields a refresh token");
        header.Should().NotContain("error=", because: "no token was presented, so this is not an invalid_token case");
    }

    [Fact]
    public async Task MalformedTokenShouldReportInvalidToken()
    {
        // arrange/act
        var response = await SendInitialize(authorization: "Bearer not-a-valid-session-id");

        // assert
        response.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
    }

    private async Task<HttpResponseMessage> SendInitialize(string? authorization)
    {
        var baseUri = Tester.UrlMapper.BaseUri;
        using var http = Tester.AppHost.NewHttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/mcp"));
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return await http.SendAsync(request);
    }
}
