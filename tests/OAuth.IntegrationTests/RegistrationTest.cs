using System.Net;

namespace ActualChat.OAuth.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public class RegistrationTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task RegisterShouldIssuePublicClientId()
    {
        // act
        var (status, doc) = await Register(new {
            client_name = "Test Client",
            redirect_uris = new[] { "https://client.example/cb", "http://localhost/cb" },
            grant_types = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_method = "none",
        });

        // assert
        status.Should().Be(HttpStatusCode.Created);
        doc.GetProperty("client_id").GetString().Should().NotBeNullOrEmpty();
        doc.GetProperty("client_name").GetString().Should().Be("Test Client");
        doc.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        doc.GetProperty("redirect_uris").GetArrayLength().Should().Be(2);
        doc.GetProperty("client_id_issued_at").GetInt64().Should().BePositive();
        doc.TryGetProperty("client_secret", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://client.example/cb", "invalid_redirect_uri")] // plain http, not loopback
    [InlineData("https://client.example/cb#frag", "invalid_redirect_uri")]
    public async Task RegisterShouldRejectBadRedirectUris(string uri, string error)
    {
        // act
        var (status, doc) = await Register(new { redirect_uris = new[] { uri }, token_endpoint_auth_method = "none" });

        // assert
        status.Should().Be(HttpStatusCode.BadRequest);
        doc.GetProperty("error").GetString().Should().Be(error);
    }

    [Fact]
    public async Task RegisterShouldRejectConfidentialClients()
    {
        // act
        var (status, doc) = await Register(new {
            redirect_uris = new[] { "https://c.example/cb" },
            token_endpoint_auth_method = "client_secret_post",
        });

        // assert
        status.Should().Be(HttpStatusCode.BadRequest);
        doc.GetProperty("error").GetString().Should().Be("invalid_client_metadata");
    }

    [Fact]
    public async Task RegisterShouldRejectUnknownGrantTypes()
    {
        // act
        var (status, doc) = await Register(new {
            redirect_uris = new[] { "https://c.example/cb" },
            grant_types = new[] { "implicit" },
        });

        // assert
        status.Should().Be(HttpStatusCode.BadRequest);
        doc.GetProperty("error").GetString().Should().Be("invalid_client_metadata");
    }
}
