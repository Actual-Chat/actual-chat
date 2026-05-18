using System.Net.Http.Headers;
using ActualChat.Testing.Host;
using ModelContextProtocol.Client;

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
            .Call(new ActualChat.Users.SessionsBackend_Upsert(SessionExt.NewApiKey()));
        unboundKey.Session.Kind.Should().Be(SessionKind.ApiKey);

        var connect = CreateClientWithRawKey(unboundKey.Session.Id).AsAsyncFunc();
        await connect.Should().ThrowAsync<HttpRequestException>();
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
