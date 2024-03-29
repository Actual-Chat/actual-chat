namespace ActualChat.Collections;

public readonly struct BidirectionalMap<TFrom, TTo>
    where TFrom : notnull
    where TTo : notnull
{
    public IReadOnlyDictionary<TFrom, TTo> Forward { get; }
    public IReadOnlyDictionary<TTo, TFrom> Backward { get; }

    public BidirectionalMap(IEnumerable<TFrom> items, Func<TFrom, TTo> mapper)
    {
        Forward = items.ToDictionary(x => x, mapper.Invoke);
        Backward = Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    }
}
