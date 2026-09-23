namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public class DiscoveryTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData("/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task DiscoveryDocumentShouldAdvertiseMcpRequirements(string path)
    {
        // act
        var doc = await GetJson(path);

        // assert
        doc.GetProperty("authorization_endpoint").GetString()
            .Should().Be(new Uri(BaseUri, "/oauth/authorize").ToString());
        doc.GetProperty("token_endpoint").GetString()
            .Should().Be(new Uri(BaseUri, "/oauth/token").ToString());
        doc.GetProperty("registration_endpoint").GetString()
            .Should().Be(new Uri(BaseUri, "/oauth/register").ToString());
        doc.GetProperty("client_id_metadata_document_supported").GetBoolean().Should().BeTrue();
        doc.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("S256");
        doc.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("none");
        doc.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString())
            .Should().Contain(["mcp", "offline_access"]);
        doc.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString())
            .Should().Contain(["authorization_code", "refresh_token"]);
    }

    [Fact]
    public async Task IssuerShouldMatchProtectedResourceAuthorizationServer()
    {
        // act
        var authorizationServer = await GetJson("/.well-known/oauth-authorization-server");
        var protectedResource = await GetJson("/.well-known/oauth-protected-resource/mcp");

        // assert
        protectedResource.GetProperty("authorization_servers")[0].GetString().Should().Be(
            authorizationServer.GetProperty("issuer").GetString(),
            because: "RFC 8414 §3.3 makes clients reject metadata whose issuer differs from the identifier they used");
    }
}
