namespace ActualChat.Users.UnitTests;

public class ClaimMapperTest
{
    [Fact]
    public void Populate_Should_Transform_Default_GitHubClaims()
    {
        var claimMapper = new ClaimMapper();
        var user = new User(Symbol.Empty, "");
        var claims = new Dictionary<string, string>(StringComparer.Ordinal) {
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","vchirikov"},
            {"urn:github:name","Vladimir Chirikov"},
        };
        user = claimMapper.UpdateClaims(user, claims);

        user.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Populate_Should_Transform_Default_MicrosoftClaims()
    {
        var claimMapper = new ClaimMapper();
        var user = new User(Symbol.Empty, "");
        var claims = new Dictionary<string, string>(StringComparer.Ordinal) {
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","vchirikov"},
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname","Chirikov"},
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname","Vladimir"},
        };
        user = claimMapper.UpdateClaims(user, claims);

        user.Name.Should().NotBeNullOrEmpty();
    }
}
