using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class AudioActivitySourceTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");

    [Fact]
    public void NoArmedChatsShouldResolveToNull()
    {
        AudioActivitySource.ResolveArmedChat([], new Dictionary<ChatId, Moment>(), T0, Window)
            .Should().BeNull();
    }

    [Fact]
    public void ArmedWithoutIncomingVoiceShouldHaveNoAnswerWindow()
    {
        var resolved = AudioActivitySource.ResolveArmedChat(
            [ChatA, ChatB], new Dictionary<ChatId, Moment>(), T0, Window);

        resolved.Should().Be((ChatA, 1, (Moment?)null));
    }

    [Fact]
    public void RecentIncomingVoiceShouldOpenAnswerWindowUntilItsEnd()
    {
        var at = T0 - TimeSpan.FromSeconds(5);
        var last = new Dictionary<ChatId, Moment> { [ChatB] = at };

        var resolved = AudioActivitySource.ResolveArmedChat([ChatA, ChatB], last, T0, Window);

        resolved.Should().Be((ChatB, 1, (Moment?)(at + Window)),
            "the answering chat owns the window, and it ends one answerWindow after its last voice");
    }

    [Fact]
    public void StaleIncomingVoiceShouldNotOpenAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatB] = T0 - TimeSpan.FromSeconds(400) };

        var resolved = AudioActivitySource.ResolveArmedChat([ChatA, ChatB], last, T0, Window);

        resolved.Should().Be((ChatA, 1, (Moment?)null));
    }

    [Fact]
    public void LatestSpeakerShouldWinTheAnswerWindow()
    {
        var atA = T0 - TimeSpan.FromSeconds(10);
        var atB = T0 - TimeSpan.FromSeconds(2);
        var last = new Dictionary<ChatId, Moment> { [ChatA] = atA, [ChatB] = atB };

        var resolved = AudioActivitySource.ResolveArmedChat([ChatA, ChatB], last, T0, Window);

        resolved.Should().Be((ChatB, 1, (Moment?)(atB + Window)));
    }
}
