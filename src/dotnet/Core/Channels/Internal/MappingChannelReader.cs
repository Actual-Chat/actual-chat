namespace ActualChat.Channels.Internal;

public sealed class MappingChannelReader<T, TFrom>(ChannelReader<TFrom> source, Func<TFrom, T> mapper)
    : ChannelReader<T>
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
        => source.WaitToReadAsync(cancellationToken);
}
