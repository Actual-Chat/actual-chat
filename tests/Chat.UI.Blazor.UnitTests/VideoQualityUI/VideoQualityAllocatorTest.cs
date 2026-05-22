using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class VideoQualityAllocatorTest
{
    private static StreamAllocationRequest Req(
        string id,
        long[] rates,
        int layerCountCap = -1,
        double renderArea = 1)
        => new(id, rates, layerCountCap < 0 ? rates.Length : layerCountCap, renderArea);

    [Fact]
    public void Primary_PicksHighestLayerThatFits()
    {
        var primary = Req("P", [100, 300, 1000]); // 3 layers
        var result = VideoQualityAllocator.Allocate(500, [primary], []);

        result["P"].LayerId.Should().Be(1, "300 fits in 500; 1000 doesn't");
    }

    [Fact]
    public void Primary_HonoursLayerCountCap()
    {
        var primary = Req("P", [100, 300, 1000], layerCountCap: 1);
        var result = VideoQualityAllocator.Allocate(10_000, [primary], []);

        result["P"].LayerId.Should().Be(0);
    }

    [Fact]
    public void SecondariesFloor_IsReservedBeforePrimary()
    {
        var primary = Req("P", [100, 1000]);
        var s1 = Req("S1", [200]);
        var s2 = Req("S2", [400]);
        // floorBudget = 600; budget = 650; primaryBudget = 50 → primary doesn't fit.
        var result = VideoQualityAllocator.Allocate(650, [primary], [s1, s2]);

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
        var s = Req("S", [200]);
        var result = VideoQualityAllocator.Allocate(0, [], [s]);

        result.ContainsKey("S").Should().BeTrue("secondaries always get at least their floor");
        result["S"].LayerId.Should().Be(0);
    }

    [Fact]
    public void EmptyPredictedRates_DoesNotCrash()
    {
        var primary = Req("P", []);
        var result = VideoQualityAllocator.Allocate(1000, [primary], []);

        result.ContainsKey("P").Should().BeFalse();
    }
}
