using ActualChat.Chat.Events;

namespace ActualChat.Chat;

public interface IChatEventStreamer1<T>
    where T: IChatEvent
{
    Task Publish(T chatEvent, CancellationToken cancellationToken);
    IAsyncEnumerable<(T Event, string Position)> Read(string streamPosition, CancellationToken cancellationToken);
}
