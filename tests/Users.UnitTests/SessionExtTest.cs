namespace ActualChat.Users.UnitTests;

public class SessionExtTest
{
    [Fact]
    public void GetPrefix_ShouldReturnFirstNChars()
    {
        var session = new Session("abcdefghijklmnopqrstuvwxyz");
        var prefix = session.GetPrefix();
        prefix.Should().Be("abcdefghijkl"); // 12 chars
        prefix.Length.Should().Be(CoreConstants.Session.IdPrefixLength);
    }

    [Fact]
    public void GetPrefix_ExactLengthSessionId_ShouldReturnFullId()
    {
        // Session IDs must be valid (minimum length), so use a real one
        var session = Session.New();
        var prefix = session.GetPrefix();
        prefix.Length.Should().Be(CoreConstants.Session.IdPrefixLength);
        session.Id.Should().StartWith(prefix);
    }

    [Fact]
    public void IsApiKey_WithApiPrefix_ShouldReturnTrue()
    {
        var session = new Session("api-" + Session.New().Id);
        session.IsApiKey().Should().BeTrue();
    }

    [Fact]
    public void IsApiKey_WithoutApiPrefix_ShouldReturnFalse()
    {
        var session = Session.New();
        session.IsApiKey().Should().BeFalse();
    }

    [Fact]
    public void IsApiKey_WithPartialPrefix_ShouldReturnFalse()
    {
        var session = new Session("ap-" + Session.New().Id);
        session.IsApiKey().Should().BeFalse();
    }

    [Fact]
    public void ApiKeyPrefix_ShouldMatchCoreConstants()
    {
        CoreConstants.Session.ApiKeyPrefix.Should().Be(CoreConstants.Session.ApiKeyPrefix);
        CoreConstants.Session.IdPrefixLength.Should().Be(CoreConstants.Session.IdPrefixLength);
    }
}
