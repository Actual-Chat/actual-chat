namespace ActualChat.Internal;

public sealed class MappingChannelReader<T, TFrom>(
    ChannelReader<TFrom> source,
    Func<TFrom, T> mapper,
    CancellationToken goneToken
    ) : ChannelReader<T>
{
    public override bool TryRead(out T item)
    {
        if (source.TryRead(out var sourceItem)) {
            item = mapper.Invoke(sourceItem);
            return true;
        }
        item = default!;
        return false;
    }

    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled)
            return source.WaitToReadAsync(goneToken);
        if (!goneToken.CanBeCanceled)
            return source.WaitToReadAsync(cancellationToken);

        return WaitToReadAsync(source, cancellationToken, goneToken);
    }

    // Private methods

    private static async ValueTask<bool> WaitToReadAsync(
        ChannelReader<TFrom> source,
        CancellationToken cancellationToken1,
        CancellationToken cancellationToken2)
    {
        using var lts = cancellationToken1.LinkWith(cancellationToken2);
        return await source.WaitToReadAsync(lts.Token).ConfigureAwait(false);
    }
}
