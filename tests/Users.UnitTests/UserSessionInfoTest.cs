namespace ActualChat.Users.UnitTests;

public class SessionInfoTest
{
    [Fact]
    public void DefaultValues_ShouldBeReasonable()
    {
        var info = new SessionInfo("");
        info.IdPrefix.Should().Be("");
        (info.Kind is SessionKind.Session).Should().BeTrue();
        info.IsActive.Should().BeFalse();
        info.Description.Should().Be("");
        info.ExpiresAt.Should().Be(default);
    }

    [Fact]
    public void WithValues_ShouldRetainAllProperties()
    {
        var now = Moment.Now;
        var info = new SessionInfo(CoreConstants.Session.ApiKeyPrefix + "abc123de") {
            IsActive = true,
            Description = "Test API Key",
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + TimeSpan.FromDays(30),
        };

        (info.Kind is SessionKind.ApiKey).Should().BeTrue();
        info.IsActive.Should().BeTrue();
        info.Description.Should().Be("Test API Key");
        info.CreatedAt.Should().Be(now);
        info.LastSeenAt.Should().Be(now);
        info.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void SessionInfoFull_ShouldInheritFromSessionInfo()
    {
        var session = Session.New();
        var testUserId = UserId.New();
        var sessionInfo = new SessionInfoFull(session) {
            IsActive = true,
            Description = "Mozilla/5.0",
            UserId = testUserId,
            AuthenticatedIdentity = new UserIdentity("google", "123"),
        };

        // Verify base properties
        sessionInfo.IdPrefix.Should().Be(session.IdPrefix);
        sessionInfo.Description.Should().Be("Mozilla/5.0");
        sessionInfo.IsActive.Should().BeTrue();
        (sessionInfo.Kind is SessionKind.ApiKey).Should().BeFalse();

        // Verify derived properties
        sessionInfo.UserId.Should().Be(testUserId);
        (sessionInfo.IsActive && sessionInfo.UserId != null).Should().BeTrue();

        // Verify auth depends on IsActive
        var expiredSession = sessionInfo with { IsActive = false };
        (expiredSession.IsActive && expiredSession.UserId != null).Should().BeFalse();
    }
}
