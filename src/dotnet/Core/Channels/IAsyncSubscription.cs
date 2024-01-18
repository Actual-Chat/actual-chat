namespace ActualChat.Channels;

public interface IAsyncSubscription<T> : IAsyncDisposable
{
    ChannelReader<T> Reader { get; }
}
