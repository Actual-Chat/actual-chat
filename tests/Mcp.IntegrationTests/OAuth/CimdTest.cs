using System.Net;
using ActualChat.OAuth;
using ActualChat.OAuth.Handlers;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

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
    public async Task NonJsonContentTypeShouldBeInvalidClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = cimd.Publish("plain", url => new {
            client_id = url,
            client_name = "Plain",
            redirect_uris = new[] { RedirectUri },
        }, contentType: "text/plain");

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client",
            because: "a metadata document must be served as application/json");
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2606:4700::1111", true)]
    [InlineData("::ffff:8.8.8.8", true)]
    public void IsPublicAddressShouldRejectInternalRanges(string address, bool isPublic)
        => CimdClientResolver.IsPublicAddress(IPAddress.Parse(address)).Should().Be(isPublic,
            because: "a metadata fetch must only reach public hosts");

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
