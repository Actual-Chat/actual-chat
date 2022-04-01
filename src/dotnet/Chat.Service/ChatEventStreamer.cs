using ActualChat.Chat.Events;

namespace ActualChat.Chat;

// TODO(AK): reimplement with redis streams
public sealed class ChatEventStreamer<T>: IChatEventStreamer<T> where T: IChatEvent
{
    private readonly Channel<T> _eventChannel;

    public ChatEventStreamer()
        => _eventChannel = Channel.CreateBounded<T>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public async Task Publish(T chatEvent, CancellationToken cancellationToken)
        => await _eventChannel.Writer.WriteAsync(chatEvent, cancellationToken).ConfigureAwait(false);

    public IAsyncEnumerable<(T Event, string Position)> Read(string streamPosition, CancellationToken cancellationToken)
        => _eventChannel.Reader.ReadAllAsync(cancellationToken).Select(v => (v, "0-0"));
}
