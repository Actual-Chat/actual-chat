namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListStatistics
{
    /// <summary>
    /// Estimated item size.
    /// </summary>
    double ItemSize { get; }
    /// <summary>
    /// Estimated response fulfillment ratio.
    /// </summary>
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

    /// <summary>
    /// Once item count reaches that value, it's reset to
    /// <see cref="ItemCountResetThreshold"/>, and the item size
    /// sum is adjusted proportionally.<br/>
    /// This allows to introduce exponentially decaying weights
    /// to the <see cref="ItemSize"/> statistics.
    /// </summary>
    public long ItemCountResetThreshold { get; init; } = 1000;
    /// <summary>
    /// Once item count reaches <see cref="ItemCountResetThreshold"/>,
    /// it's reset to this value, and the item size
    /// sum is adjusted proportionally.<br/>
    /// This allows to introduce exponentially decaying weights
    /// to the <see cref="ItemSize"/> statistics.
    /// </summary>
    public long ItemCountResetValue { get; init; } = 900;

    /// <inheritdoc />
    public double ItemSize => _itemSizeSum / _itemCount;

    /// <summary>
    /// Acts similarly to <see cref="ItemCountResetThreshold"/>, but for response count statistics.
    /// </summary>
    public long ResponseCountResetThreshold { get; init; } = 10;
    /// <summary>
    /// Acts similarly to <see cref="ItemCountResetValue"/>, but for response count statistics.
    /// </summary>
    public long ResponseCountResetValue { get; init; } = 8;

    /// <inheritdoc />
    public double ResponseFulfillmentRatio => _responseFulfillmentRatioSum / _responseCount;

    public void AddItem(double size)
    {
        _itemSizeSum += size;
        ++_itemCount;
        if (_itemCount < ItemCountResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        _itemSizeSum *= (double)ItemCountResetValue / ItemCountResetThreshold;
        _itemCount = ItemCountResetValue;
    }

    public void RemoveItem(double size)
    {
        if (_itemCount <= 0) return;

        _itemSizeSum -= size;
        _itemCount--;
    }

    public void AddResponse(double fulfillmentRatio)
    {
        _responseFulfillmentRatioSum += fulfillmentRatio;
        ++_responseCount;
        if (_responseCount < ResponseCountResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        _responseFulfillmentRatioSum *= (double)ResponseCountResetValue / ResponseCountResetThreshold;
        _responseCount = ResponseCountResetValue;
    }
}
