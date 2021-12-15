namespace ActualChat.UI.Blazor.UnitTests;

public class VirtualListStatisticsTest
{
    [Fact(Skip = "Fix later")]
    public void AddItem_Should_Not_Return_Average()
    {
        const int treshold = 10;
        const int reset = 5;
        var stats = new VirtualListStatistics() {
            ItemCountResetThreshold = treshold,
            ItemCountResetValue = reset,
        };

        Assert.Equal(0, stats.ItemSize);

        for (int i = 0; i < treshold + 5; ++i) {
            stats.AddItem(10.0d);
            Assert.Equal(10.0d, stats.ItemSize);
        }
    }

    [Fact(Skip = "Fix later")]
    public void AddResponse_Should_Not_Return_Average()
    {
        const int treshold = 5;
        const int reset = 3;
        var stats = new VirtualListStatistics() {
            ResponseExpectedCountSumResetThreshold = treshold,
            ResponseExpectedCountSumResetValue = reset,
        };

        Assert.Equal(0, stats.ResponseFulfillmentRatio);

        for (int i = 0; i < treshold + 5; ++i) {
            stats.AddResponse(100, 10);
            Assert.Equal(10.0d, stats.ResponseFulfillmentRatio);
        }
    }
}
