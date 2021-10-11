using ActualChat.UI.Blazor.Components;
namespace ActualChat.UI.Blazor.UnitTests;

public class VirtualListStatisticsTests
{
    [Fact]
    public void AddItem_Should_Not_Return_Average()
    {
        const int treshold = 10;
        const int reset = 5;
        var stats = new VirtualListStatistics() {
            ItemCountResetThreshold = treshold,
            ItemCountResetValue = reset,
        };

        Assert.Equal(0, stats.ItemSizeEstimate);

        for (int i = 0; i < treshold + 5; ++i) {
            stats.AddItem(10.0d);
            Assert.Equal(10.0d, stats.ItemSizeEstimate);
        }
    }

    [Fact]
    public void AddResponse_Should_Not_Return_Average()
    {
        const int treshold = 5;
        const int reset = 3;
        var stats = new VirtualListStatistics() {
            ResponseCountResetThreshold = treshold,
            ResponseCountResetValue = reset,
        };

        Assert.Equal(0, stats.ResponseFulfillmentRatio);

        for (int i = 0; i < treshold + 5; ++i) {
            stats.AddResponse(10.0d);
            Assert.Equal(10.0d, stats.ResponseFulfillmentRatio);
        }
    }
}
