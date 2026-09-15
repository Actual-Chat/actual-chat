namespace ActualChat.Core.UnitTests;

public class SessionExtTest
{
    [Fact]
    public void NewOAuthShouldBeOAuthKind()
    {
        // act
        var session = SessionExt.NewOAuth();

        // assert
        session.Kind.Should().Be(SessionKind.OAuth);
        session.Id[0].Should().Be(CoreConstants.Session.OAuthPrefix);
        SessionExt.IsValidId(session.Id).Should().BeTrue();
    }

    [Fact]
    public void KindShouldFollowPrefix()
    {
        // act & assert
        SessionExt.NewApiKey().Kind.Should().Be(SessionKind.ApiKey);
        new Session("abcdefghijklmnopqrstuvwxyz").Kind.Should().Be(SessionKind.Session);
    }
}
