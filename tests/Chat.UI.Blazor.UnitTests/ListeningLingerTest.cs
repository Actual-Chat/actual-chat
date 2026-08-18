using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ListeningLingerTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan ListenerTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void ListenerSessionUsesConstantTimeout()
    {
        // act + assert
        ChatAudioUI.ComputeStopListeningAt(T0, false, ListenerTimeout, TimeSpan.Zero)
            .Should().Be(T0 + ListenerTimeout);
        ChatAudioUI.ComputeStopListeningAt(T0, false, ListenerTimeout, TimeSpan.FromSeconds(10))
            .Should().Be(T0 + ListenerTimeout);
    }

    [Fact]
    public void SpeakerSessionUsesSetting()
    {
        // act + assert
        ChatAudioUI.ComputeStopListeningAt(T0, true, ListenerTimeout, TimeSpan.FromSeconds(10))
            .Should().Be(T0 + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void SpeakerSessionWithOffStopsAtActivityEdge()
    {
        // act + assert
        ChatAudioUI.ComputeStopListeningAt(T0, true, ListenerTimeout, TimeSpan.Zero)
            .Should().Be(T0);
    }
}
