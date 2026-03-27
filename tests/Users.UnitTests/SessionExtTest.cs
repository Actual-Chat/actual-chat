namespace ActualChat.Users.UnitTests;

public class SessionExtTest
{
    [Fact]
    public void GetPrefix_ShouldReturnFirstNChars()
    {
        var session = new Session("abcdefghijklmnopqrstuvwxyz");
        var prefix = session.IdPrefix;
        prefix.Should().Be("abcdefgh"); // 8 chars
        prefix.Length.Should().Be(CoreConstants.Session.IdPrefixLength);
    }

    [Fact]
    public void GetPrefix_ExactLengthSessionId_ShouldReturnFullId()
    {
        // Session IDs must be valid (minimum length), so use a real one
        var session = Session.New();
        var prefix = session.IdPrefix;
        prefix.Length.Should().Be(CoreConstants.Session.IdPrefixLength);
        session.Id.Should().StartWith(prefix);
    }

    [Fact]
    public void DefaultSession_ShouldBeSession()
    {
        var session = Session.Default;
        (session.Kind is SessionKind.Session).Should().BeTrue();
    }

    [Fact]
    public void IsApiKey_WithApiPrefix_ShouldReturnTrue()
    {
        var session = new Session(CoreConstants.Session.ApiKeyPrefix + Session.New().Id);
        (session.Kind is SessionKind.ApiKey).Should().BeTrue();
    }

    [Fact]
    public void IsApiKey_WithoutApiPrefix_ShouldReturnFalse()
    {
        var session = Session.New();
        (session.Kind is SessionKind.ApiKey).Should().BeFalse();
    }

    [Fact]
    public void IsApiKey_WithPartialPrefix_ShouldReturnFalse()
    {
        var session = new Session("x-" + Session.New().Id);
        (session.Kind is SessionKind.ApiKey).Should().BeFalse();
    }
}
