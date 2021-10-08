namespace ActualChat.UI.Blazor.Components;

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
    private long _itemCount;
    private double _itemSizeSum;

    private long _responseCount;
    private double _responseFulfillmentRatioSum;

    public long ItemCountResetThreshold { get; init; } = 1000;
    /// <summary>
    /// Number of remainig items after <see cref="ItemCountResetThreshold"/> is reached. <br />
    /// Should be less than threshold.
    /// </summary>
    public long ItemCountResetValue { get; init; } = 900;

    private double _itemSizeEstimate;
    /// <inheritdoc />
    public double ItemSizeEstimate => _itemSizeEstimate;

    public long ResponseCountResetThreshold { get; init; } = 10;

    /// <summary>
    /// Number of remainig items after <see cref="ResponseCountResetThreshold"/> is reached. <br />
    /// Should be less than threshold.
    /// </summary>
    public long ResponseCountResetValue { get; init; } = 8;

    private double _responseFulfillmentRatio;

    /// <inheritdoc />
    public double ResponseFulfillmentRatio => _responseFulfillmentRatio;

    public void AddItem(double size)
    {
        _itemSizeSum += size;
        ++_itemCount;

        if (_itemCount >= ItemCountResetThreshold) {
            // we change the item count too, so remaining items will have increased weight
            _itemSizeSum *= (double)ItemCountResetValue / ItemCountResetThreshold;
            _itemCount = ItemCountResetValue;
        }
        _itemSizeEstimate = _itemSizeSum / _itemCount;
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
        ++_responseCount;
        if (_responseCount >= ResponseCountResetThreshold) {
            // we change the item count too, so remaining items will have increased weight
            _responseFulfillmentRatioSum *= (double)ResponseCountResetValue / ResponseCountResetThreshold;
            _responseCount = ResponseCountResetValue;
        }
        _responseFulfillmentRatio = _responseFulfillmentRatioSum / _responseCount;
    }
}
