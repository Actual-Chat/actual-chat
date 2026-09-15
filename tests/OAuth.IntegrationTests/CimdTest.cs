using System.Net;
using ActualChat.OAuth.Handlers;
using ActualChat.Testing.Host;

namespace ActualChat.OAuth.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public sealed class CimdTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private const string RedirectUri = "https://claude.example/cb";

    [Fact]
    public async Task UrlClientIdShouldRegisterFromMetadataDocument()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = PublishDocument(cimd, "claude", "Claude");

        // act
        var response = await Authorize(clientId, RedirectUri, NewPkce());
        var info = await Tester.AppServices.GetRequiredService<IOAuthGrants>()
            .GetClient(Tester.Session, clientId, default);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        GetQueryValue(response.Headers.Location!, "code").Should().NotBeEmpty();
        info.Should().NotBeNull(because: "the metadata document registers the client just in time");
        info!.ClientName.Should().Be("Claude");
        info.RedirectHosts.Should().Contain("claude.example");
    }

    [Fact]
    public async Task MismatchedClientIdInsideDocumentShouldBeInvalidClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = cimd.Publish("bad", _ => new {
            client_id = "https://somewhere.else/x.json",
            redirect_uris = new[] { RedirectUri },
        });

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect,
            because: "a client whose document names another client_id has no trustworthy redirect URI");
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task UnreachableDocumentShouldBeInvalidClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = "http://127.0.0.1:1/cimd/none.json";

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task OversizedDocumentShouldBeInvalidClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = cimd.Publish("huge", url => new {
            client_id = url,
            client_name = new string('x', CimdClientResolver.MaxDocumentLength),
            redirect_uris = new[] { RedirectUri },
        });

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client",
            because: "documents above the size cap are never parsed");
    }

    [Fact]
    public async Task DocumentShouldBeCachedBetweenRequests()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = PublishDocument(cimd, "cached", "C");
        var first = await Authorize(clientId, RedirectUri, NewPkce());
        var fetches = cimd.FetchCount;

        // act
        var second = await Authorize(clientId, RedirectUri, NewPkce());

        // assert
        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        second.StatusCode.Should().Be(HttpStatusCode.Redirect);
        fetches.Should().BeGreaterThan(0, because: "the first request must fetch the document");
        cimd.FetchCount.Should().Be(fetches, because: "the document is cached for CimdCacheAge");
    }

    // Private methods

    private static string PublishDocument(CimdTestServer cimd, string name, string clientName)
        => cimd.Publish(name, url => new {
            client_id = url,
            client_name = clientName,
            redirect_uris = new[] { RedirectUri },
        });
}
