using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ReplyTargetResolverTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(150);
    private static ChatId Chat(string s) => ChatId.Parse(s);

    [Fact]
    public void PicksMostRecentSpeakerWithinWindow()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var b = Chat("bbbbbbbbbbbbbbbbbbbb");
        var armed = new[] { a, b };
        var last = new Dictionary<ChatId, Moment> { [a] = T0 - TimeSpan.FromSeconds(90), [b] = T0 - TimeSpan.FromSeconds(20) };
        ReplyTargetResolver.Resolve(armed, last, focusedChatId: null, T0, Window).Should().Be(b);
    }

    [Fact]
    public void IgnoresSpeakersOutsideWindow_FallsBackToFocused()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var b = Chat("bbbbbbbbbbbbbbbbbbbb");
        var armed = new[] { a, b };
        var last = new Dictionary<ChatId, Moment> { [a] = T0 - TimeSpan.FromSeconds(400) };
        ReplyTargetResolver.Resolve(armed, last, focusedChatId: b, T0, Window).Should().Be(b);
    }

    [Fact]
    public void FocusedFallbackOnlyIfArmed()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var other = Chat("cccccccccccccccccccc");
        var armed = new[] { a };
        ReplyTargetResolver.Resolve(armed, new Dictionary<ChatId, Moment>(), focusedChatId: other, T0, Window)
            .Should().Be(a);
    }

    [Fact]
    public void SoleArmedFallback()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa");
        ReplyTargetResolver.Resolve(new[] { a }, new Dictionary<ChatId, Moment>(), focusedChatId: null, T0, Window)
            .Should().Be(a);
    }

    [Fact]
    public void AmbiguousColdStart_ReturnsNull()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var b = Chat("bbbbbbbbbbbbbbbbbbbb");
        ReplyTargetResolver.Resolve(new[] { a, b }, new Dictionary<ChatId, Moment>(), focusedChatId: null, T0, Window)
            .Should().BeNull();
    }

    [Fact]
    public void NoArmedChats_ReturnsNull()
    {
        ReplyTargetResolver.Resolve(Array.Empty<ChatId>(), new Dictionary<ChatId, Moment>(), null, T0, Window)
            .Should().BeNull();
    }
}
