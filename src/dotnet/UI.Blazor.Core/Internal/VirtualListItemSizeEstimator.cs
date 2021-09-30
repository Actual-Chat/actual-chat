namespace ActualChat.UI.Blazor.Internal;

public interface IVirtualListStatistics
{
    double ItemSizeEstimate { get; }
    double ResponseFulfillmentRatio { get; }

    void AddItem(double size);
    void AddResponse(double fulfillmentRatio);
    void RemoveItem(double size);
}

public class VirtualListStatistics : IVirtualListStatistics
{
    private long _itemCount = 1;
    private double _itemSizeSum = 16;
    private long _responseCount = 1;
    private double _responseFulfillmentRatioSum = 1;

    public long ItemCountResetThreshold { get; init; } = 1000;
    public long ItemCountResetValue { get; init; } = 900;
    public long ResponseCountResetThreshold { get; init; } = 10;
    public long ResponseCountResetValue { get; init; } = 8;

    public double ItemSizeEstimate => _itemSizeSum / _itemCount;
    public double ResponseFulfillmentRatio => _responseFulfillmentRatioSum / _responseCount;

    public void AddItem(double size)
    {
        _itemSizeSum += size;
        _itemCount++;
        if (_itemCount >= ItemCountResetThreshold) {
            _itemCount = ItemCountResetValue;
            _itemSizeSum *= (double) ItemCountResetValue / ItemCountResetThreshold;
        }
    }

    public void RemoveItem(double size)
    {
        if (_itemCount <= 0)
            return;
        _itemSizeSum -= size;
        _itemCount--;
    }

    public void AddResponse(double fulfillmentRatio)
    {
        _responseFulfillmentRatioSum += fulfillmentRatio;
        _responseCount++;
        if (_responseCount >= ResponseCountResetThreshold) {
            _responseCount = ResponseCountResetValue;
            _responseFulfillmentRatioSum *= (double) ResponseCountResetValue / ResponseCountResetThreshold;
        }
    }
}
