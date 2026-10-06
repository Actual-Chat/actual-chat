namespace ActualChat;

public static class ChannelOptionsExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Channel<T> NewChannel<T>(this ChannelOptions options)
        => ChannelExt.New<T>(options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Channel<T> NewChannel<T>(this BoundedChannelOptions options)
        => Channel.CreateBounded<T>(options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Channel<T> NewChannel<T>(this UnboundedChannelOptions options)
        => Channel.CreateUnbounded<T>(options);
}
