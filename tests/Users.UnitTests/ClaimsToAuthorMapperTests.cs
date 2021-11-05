using ActualChat.Users.Db;

namespace ActualChat.Users.UnitTests;

public class ClaimsToAuthorMapperTests
{
    [Fact]
    public async Task Populate_Should_Transform_Default_GitHubClaims()
    {
        DbUser user = new();
        DbUserAuthor author = new();
        var claims = new Dictionary<string, string>() {
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","vchirikov"},
            {"urn:github:name","Vladimir Chirikov"},
        };
        var claimMapper = new ClaimMapper();
        await claimMapper.Apply(null!, user, author, claims, default).ConfigureAwait(false);

        author.Name.Should().NotBeNullOrEmpty();
        author.Nickname.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Populate_Should_Transform_Default_MicrosoftClaims()
    {
        DbUser user = new();
        DbUserAuthor author = new();
        var claims = new Dictionary<string, string>() {
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","vchirikov"},
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname","Chirikov"},
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname","Vladimir"},
        };
        var claimMapper = new ClaimMapper();
        await claimMapper.Apply(null!, user, author, claims, default).ConfigureAwait(false);

        author.Name.Should().NotBeNullOrEmpty();
        author.Nickname.Should().NotBeNullOrEmpty();
    }
}
