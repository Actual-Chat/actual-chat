using StackExchange.Redis;

namespace ActualChat.Redis;

public static class ChannelMessageQueueExt
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref Channel<ChannelMessage> QueueGetter(ChannelMessageQueue @this);

    public static ChannelReader<ChannelMessage> GetQueueReader(this ChannelMessageQueue queue)
        => QueueGetter(queue).Reader;
}
