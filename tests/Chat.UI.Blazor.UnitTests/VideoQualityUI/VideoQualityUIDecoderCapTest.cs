using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class VideoQualityUIDecoderCapTest
{
    [Fact]
    public void DemoteOnEdge_BadAfterGood_SetsCap()
    {
        var s = new DecoderCapState();
        var cap = s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        cap.Should().BeNull();
        cap = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        cap.Should().Be(1); // requestedLayer=2 → cap=max(0, 2-1)=1
    }

    [Fact]
    public void DemoteOnEdge_RepeatedBad_DoesNotWalkDown()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        var cap1 = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap2 = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap3 = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        cap1.Should().Be(1);
        cap2.Should().Be(1);
        cap3.Should().Be(1);
    }

    [Fact]
    public void GoodClearsCap()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap = s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        cap.Should().BeNull();
    }

    [Fact]
    public void ReDemoteAfterGoodBadCycle_PicksFreshLayer()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);   // cap=1
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 2);  // cleared
        var cap = s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 2);
        cap.Should().Be(0); // requestedLayer=1 → cap=max(0, 1-1)=0
    }

    [Fact]
    public void MarginalHoldsExistingCap()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        var cap = s.OnVerdict("stream-a", HealthVerdict.Marginal, requestedLayerCount: 3);
        cap.Should().Be(1);
    }

    [Fact]
    public void Prune_RemovesStaleStreamState()
    {
        var s = new DecoderCapState();
        s.OnVerdict("stream-a", HealthVerdict.Good, requestedLayerCount: 3);
        s.OnVerdict("stream-a", HealthVerdict.Bad, requestedLayerCount: 3);
        s.OnVerdict("stream-b", HealthVerdict.Bad, requestedLayerCount: 3);
        s.PruneStaleStreams(new HashSet<string> { "stream-a" });
        s.HasState("stream-a").Should().BeTrue();
        s.HasState("stream-b").Should().BeFalse();
    }
}
