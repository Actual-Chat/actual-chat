using System.Net;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(OAuthStrictCollection))]
public sealed class CimdStrictTest(OAuthStrictCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthStrictCollection.AppHostFixture>(fixture, @out)
{
    private const string RedirectUri = "https://claude.example/cb";

    [Fact]
    public async Task OverlongHostShouldBeInvalidClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        // Uri accepts this 304-char host (five 60-char labels), but DNS lookup throws on anything over 255
        var host = string.Join(".", Enumerable.Repeat(new string('a', 60), 5));
        var clientId = $"https://{host}/cimd/x.json";

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "a host DNS cannot even look up is rejected as invalid_client, not surfaced as a server error");
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task PrivateHostShouldBeInvalidClient()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = "https://127.0.0.1/cimd/x.json";

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "documents on private or loopback hosts are rejected as invalid_client before any fetch");
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }
}
