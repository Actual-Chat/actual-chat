namespace ActualChat;

public interface IAsyncSubscription<T> : IAsyncDisposable
{
    ChannelReader<T> Reader { get; }
}
