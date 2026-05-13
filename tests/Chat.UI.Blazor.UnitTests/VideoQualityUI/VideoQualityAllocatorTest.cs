using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class VideoQualityAllocatorTest
{
    private static StreamAllocationRequest Req(
        string id,
        long[] rates,
        int layerCountCap = -1,
        int producerTemporal = 1,
        double renderArea = 1)
        => new(id, rates, layerCountCap < 0 ? rates.Length : layerCountCap, producerTemporal, renderArea);

    [Fact]
    public void Primary_PicksHighestLayerThatFits()
    {
        var primary = Req("P", [100, 300, 1000]); // 3 layers
        var result = VideoQualityAllocator.Allocate(500, [primary], []);

        result["P"].LayerId.Should().Be(1, "300 fits in 500; 1000 doesn't");
        result["P"].TemporalLayerId.Should().Be(1);
    }

    [Fact]
    public void Primary_HonoursLayerCountCap()
    {
        var primary = Req("P", [100, 300, 1000], layerCountCap: 1);
        var result = VideoQualityAllocator.Allocate(10_000, [primary], []);

        result["P"].LayerId.Should().Be(0);
    }

    [Fact]
    public void TemporalFraction_OneLayer_AlwaysPinnedToOne()
    {
        var primary = Req("P", [100], producerTemporal: 1);
        var result = VideoQualityAllocator.Allocate(10_000, [primary], []);

        result["P"].TemporalLayerId.Should().Be(1);
    }

    [Fact]
    public void TemporalFraction_TwoLayers_DyadicSteps()
    {
        // K=2 → fractions {0.5, 1.0} → TemporalLayerId values {1, 2}
        // PredictedRatesByLayer=[200] ⇒ rate at frac 0.5 = 100, at frac 1.0 = 200
        var primary = Req("P", [200], producerTemporal: 2);

        // budget 150 fits the 0.5 step but not 1.0
        var r150 = VideoQualityAllocator.Allocate(150, [primary], []);
        r150["P"].LayerId.Should().Be(0);
        r150["P"].TemporalLayerId.Should().Be(1, "fraction 0.5 → drop floor 1");

        // budget 250 fits full 1.0 → drop floor 2
        var r250 = VideoQualityAllocator.Allocate(250, [primary], []);
        r250["P"].TemporalLayerId.Should().Be(2);
    }

    [Fact]
    public void SecondariesFloor_IsReservedBeforePrimary()
    {
        var primary = Req("P", [100, 1000]);
        var s1 = Req("S1", [200], producerTemporal: 2); // floor = 200*0.5 = 100
        var s2 = Req("S2", [400], producerTemporal: 2); // floor = 200
        // floorBudget = 300; budget = 350; primaryBudget = 50 → primary at layer1 (rate 100) doesn't fit
        // → primary picks layer1 only at rate ... actually no rate fits. Primary should get nothing.
        var result = VideoQualityAllocator.Allocate(350, [primary], [s1, s2]);

        // Primary may not fit in primaryBudget (50)
        result.ContainsKey("P").Should().BeFalse("primaryBudget too small for primary");
        result["S1"].LayerId.Should().Be(0);
        result["S2"].LayerId.Should().Be(0);
    }

    [Fact]
    public void SecondaryShare_IsProportionalToRenderArea()
    {
        var bigSecondary = Req("BIG", [100, 1000], renderArea: 9);
        var smallSecondary = Req("SMALL", [100, 1000], renderArea: 1);

        var result = VideoQualityAllocator.Allocate(2000, [], [bigSecondary, smallSecondary]);

        var bigLayerId = result["BIG"].LayerId;
        var smallLayerId = result["SMALL"].LayerId;
        bigLayerId.Should().BeGreaterThanOrEqualTo(smallLayerId,
            "big secondary should get at least as much as small");
    }

    [Fact]
    public void EmptyPrimaries_AllocatesSecondariesFromFullBudget()
    {
        var s = Req("S", [100, 500]);
        var result = VideoQualityAllocator.Allocate(600, [], [s]);

        result["S"].LayerId.Should().Be(1);
    }

    [Fact]
    public void ZeroBudget_AllSecondariesStillGetFloor()
    {
        var s = Req("S", [200], producerTemporal: 2);
        var result = VideoQualityAllocator.Allocate(0, [], [s]);

        result.ContainsKey("S").Should().BeTrue("secondaries always get at least their floor");
        result["S"].LayerId.Should().Be(0);
        result["S"].TemporalLayerId.Should().Be(1);
    }

    [Fact]
    public void EmptyPredictedRates_DoesNotCrash()
    {
        var primary = Req("P", []);
        var result = VideoQualityAllocator.Allocate(1000, [primary], []);

        result.ContainsKey("P").Should().BeFalse();
    }
}
