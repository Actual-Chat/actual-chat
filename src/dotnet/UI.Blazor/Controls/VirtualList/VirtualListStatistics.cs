namespace ActualChat.UI.Blazor.Controls;

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

    void AddItem(double size, int count);
    void AddResponse(long actualCount, long expectedCount);
}

public class VirtualListStatistics : IVirtualListStatistics
{
    private long _itemCount;
    private double _itemSizeSum;

    private double _responseActualCountSum;
    private long _responseExpectedCountSum;

    public double DefaultItemSize { get; init; } = 100;
    public double MinItemSize { get; init; } = 8;
    public double MaxItemSize { get; init; } = 16_384;
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
    public double ItemSize
        => Math.Clamp(
            _itemCount == 0 ? DefaultItemSize : _itemSizeSum / _itemCount,
            MinItemSize, MaxItemSize);

    public double MinResponseFulfillmentRatio { get; init; } = 0.25;
    public double MaxResponseFulfillmentRatio { get; init; } = 1;
    /// <summary>
    /// Acts similarly to <see cref="ItemCountResetThreshold"/>, but for response count statistics.
    /// </summary>
    public long ResponseExpectedCountSumResetThreshold { get; init; } = 1000;
    /// <summary>
    /// Acts similarly to <see cref="ItemCountResetValue"/>, but for response count statistics.
    /// </summary>
    public long ResponseExpectedCountSumResetValue { get; init; } = 800;

    /// <inheritdoc />
    public double ResponseFulfillmentRatio
        => Math.Clamp(
            _responseExpectedCountSum < 1
                ? MaxResponseFulfillmentRatio
                : _responseActualCountSum / _responseExpectedCountSum,
            MinResponseFulfillmentRatio,
            MaxResponseFulfillmentRatio);

    public void AddItem(double size, int countAs)
    {
        if (countAs == 0)
            return;

        size /= countAs;
        _itemSizeSum += size;
        _itemCount += countAs;
        if (_itemCount < ItemCountResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        _itemSizeSum *= (double)ItemCountResetValue / _itemCount;
        _itemCount = ItemCountResetValue;
    }

    public void AddResponse(long actualCount, long expectedCount)
    {
        _responseActualCountSum += actualCount;
        _responseExpectedCountSum += expectedCount;
        if (_responseExpectedCountSum < ResponseExpectedCountSumResetThreshold) return;

        // We change the item count too, so remaining items will have increased weight
        _responseActualCountSum *= (double)ResponseExpectedCountSumResetValue / _responseExpectedCountSum;
        _responseExpectedCountSum = ResponseExpectedCountSumResetValue;
    }
}
