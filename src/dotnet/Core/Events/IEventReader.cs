namespace ActualChat.Events;

public interface IEventReader<T> where T: IEvent
{
    Task<(T Event, string Id)[]> Read(int batchSize, CancellationToken cancellationToken);

    Task Ack(string id, CancellationToken cancellationToken);
}
