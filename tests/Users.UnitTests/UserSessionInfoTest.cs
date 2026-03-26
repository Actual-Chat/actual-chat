namespace ActualChat.Users.UnitTests;

public class UserSessionInfoTest
{
    [Fact]
    public void DefaultValues_ShouldBeReasonable()
    {
        var info = new UserSessionInfo("");
        info.IdPrefix.Should().Be("");
        info.IsApiKey.Should().BeFalse();
        info.IsActive.Should().BeFalse();
        info.Name.Should().Be("");
        info.UserAgent.Should().Be("");
        info.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void WithValues_ShouldRetainAllProperties()
    {
        var now = Moment.Now;
        var info = new UserSessionInfo("abc123def456") {
            IsApiKey = true,
            IsActive = true,
            Name = "Test API Key",
            UserAgent = "TestAgent/1.0",
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + TimeSpan.FromDays(30),
        };

        info.IdPrefix.Should().Be("abc123def456");
        info.IsApiKey.Should().BeTrue();
        info.IsActive.Should().BeTrue();
        info.Name.Should().Be("Test API Key");
        info.UserAgent.Should().Be("TestAgent/1.0");
        info.CreatedAt.Should().Be(now);
        info.LastSeenAt.Should().Be(now);
        info.ExpiresAt.Should().NotBeNull();
    }
}
