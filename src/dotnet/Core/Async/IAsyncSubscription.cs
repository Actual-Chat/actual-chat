namespace ActualChat;

/// <summary>
/// Represents an active subscription to an <see cref="IAsyncObservable{T}"/>.
/// </summary>
public interface IAsyncSubscription<T> : IAsyncDisposable
{
    ChannelReader<T> Reader { get; }
}
