using ActualChat.WebHooks;

namespace ActualChat.Chat.UnitTests;

public class WebHookBotUserIdTest
{
    [Fact]
    public void BotUserIdShouldRoundTrip()
    {
        // arrange
        var hookId = WebHookId.New();

        // act
        var userId = hookId.ToBotUserId();
        var isParsed = WebHookId.TryParseBotUserId(userId, out var parsed);

        // assert
        userId.Value.Should().StartWith(Constants.WebHooks.BotUserIdPrefix);
        userId.IsGuest.Should().BeFalse();
        isParsed.Should().BeTrue();
        parsed.Should().Be(hookId);
    }

    [Theory]
    [InlineData("pKGsAk")]
    [InlineData("whin")]
    [InlineData("sherlock")]
    public void OrdinaryUserIdsShouldNotParseAsBots(string value)
    {
        // act
        var isParsed = WebHookId.TryParseBotUserId(UserId.Parse(value), out var parsed);

        // assert
        isParsed.Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void NullShouldNotParseAsBot()
        => WebHookId.TryParseBotUserId(null, out _).Should().BeFalse();
}
