using System.Net.Http.Headers;
using ActualChat.OAuth;
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

    [Fact]
    public async Task LegacyRouteShouldAcceptApiKey()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var apiKey = await IssueApiKey();

        // act
        await using var client = await CreateClientWithRawKey(apiKey, OAuthConstants.LegacyMcpResourcePath);
        var tools = await client.ListToolsAsync();

        // assert
        tools.Should().NotBeEmpty(because: "clients configured with /api/mcp must keep working");
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource", "/mcp")]
    [InlineData("/.well-known/oauth-protected-resource/mcp", "/mcp")]
    [InlineData("/.well-known/oauth-protected-resource/api/mcp", "/api/mcp")]
    public async Task ProtectedResourceDocumentShouldNameMcpEndpoint(string path, string route)
    {
        // arrange
        using var http = Tester.AppHost.NewHttpClient();

        // act
        var doc = JsonDocument.Parse(await http.GetStringAsync(path)).RootElement;

        // assert
        var baseUri = Tester.UrlMapper.BaseUri;
        doc.GetProperty("resource").GetString().Should().Be(new Uri(baseUri, route).ToString(),
            because: "RFC 9728 makes clients reject a resource that differs from the URL they connected to");
        doc.GetProperty("authorization_servers")[0].GetString().Should().Be(baseUri.ToString(),
            because: "clients compare it byte-for-byte with the AS metadata issuer, which keeps the trailing slash");
        doc.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("mcp", "offline_access");
        doc.GetProperty("bearer_methods_supported")[0].GetString().Should().Be("header");
    }

    [Fact]
    public async Task UnknownResourceDocumentShouldBeNotFound()
    {
        // arrange
        using var http = Tester.AppHost.NewHttpClient();

        // act
        var response = await http.GetAsync("/.well-known/oauth-protected-resource/api/other");

        // assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/api/mcp")]
    public async Task MissingHeaderShouldPointAtResourceMetadata(string route)
    {
        // arrange/act
        var response = await SendInitialize(authorization: null, route);

        // assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        var header = response.Headers.WwwAuthenticate.ToString();
        var metadataUrl = new Uri(Tester.UrlMapper.BaseUri, "/.well-known/oauth-protected-resource" + route);
        header.Should().Contain($"resource_metadata=\"{metadataUrl}\"",
            because: "the challenge must lead the client to the metadata of the route it connected to");
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

    private async Task<HttpResponseMessage> SendInitialize(
        string? authorization, string route = OAuthConstants.McpResourcePath)
    {
        var baseUri = Tester.UrlMapper.BaseUri;
        using var http = Tester.AppHost.NewHttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, route));
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
