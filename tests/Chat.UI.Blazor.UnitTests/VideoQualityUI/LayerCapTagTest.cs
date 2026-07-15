using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class LayerCapTagTest
{
    private const int NoCap = int.MaxValue;

    private static string Tag(
        bool hasDecoderCap = false,
        int layerId = 2,
        int demandCap = 3,
        int thermalCap = 3,
        int debugCap = NoCap)
        => VideoQualityUI.GetLayerCapTag(hasDecoderCap, layerId, demandCap, thermalCap, debugCap);

    [Fact]
    public void DecoderWinsOverEveryOtherCap()
        => Tag(hasDecoderCap: true, layerId: 0, demandCap: 1, thermalCap: 1, debugCap: 1)
            .Should().Be("decoder");

    [Fact]
    public void BwWhenAllocatorLandsBelowTheClamp()
        => Tag(layerId: 0, demandCap: 3).Should().Be("bw");

    // The L0-pinning case from the 2026-07 dev-server dump: the tile demanded a
    // single layer, so the allocator could never exceed L0 no matter the budget.
    // This used to report "bw" and sent triage after the bandwidth estimator.
    [Fact]
    public void DemandWhenRenderDemandIsTheBindingClamp()
        => Tag(layerId: 0, demandCap: 1).Should().Be("demand");

    [Fact]
    public void ThermalWhenThermalIsStricterThanDemand()
        => Tag(layerId: 0, demandCap: 3, thermalCap: 1).Should().Be("thermal");

    [Fact]
    public void DemandWhenThermalTiesDemand()
        => Tag(layerId: 0, demandCap: 1, thermalCap: 1).Should().Be("demand");

    [Fact]
    public void DebugWhenDebugOverrideIsStrictest()
        => Tag(layerId: 0, demandCap: 3, thermalCap: 3, debugCap: 1).Should().Be("debug");

    [Fact]
    public void DemandWhenDebugTiesDemand()
        => Tag(layerId: 0, demandCap: 1, thermalCap: 3, debugCap: 1).Should().Be("demand");

    // A stream missing from the entries list reports no demand clamp, so a low
    // layer is attributable to capacity rather than to a clamp we can't see.
    [Fact]
    public void BwWhenDemandIsUnknown()
        => Tag(layerId: 0, demandCap: NoCap).Should().Be("bw");

    [Fact]
    public void DemandWhenAtTopOfAnUnclampedLadder()
        => Tag(layerId: 2, demandCap: 3, thermalCap: 3).Should().Be("demand");
}
