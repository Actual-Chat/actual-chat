using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ContinuedListeningTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    [Fact]
    public void FreshListenUsesGrace()
    {
        // act + assert
        ChatAudioUI.ComputeStopListeningAt(T0, false, Grace, TimeSpan.Zero)
            .Should().Be(T0 + Grace);
        ChatAudioUI.ComputeStopListeningAt(T0, false, Grace, TimeSpan.FromSeconds(10))
            .Should().Be(T0 + Grace);
    }

    [Fact]
    public void AfterActivityUsesLinger()
    {
        // act + assert
        ChatAudioUI.ComputeStopListeningAt(T0, true, Grace, TimeSpan.FromSeconds(10))
            .Should().Be(T0 + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ZeroLingerStopsAtActivityEdge()
    {
        // act + assert
        ChatAudioUI.ComputeStopListeningAt(T0, true, Grace, TimeSpan.Zero)
            .Should().Be(T0);
    }
}
