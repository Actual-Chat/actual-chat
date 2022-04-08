namespace ActualChat.Events;

public class LocalEventHub<T> where T: class, IEvent
{
    public LocalEventHub()
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.Wait,
        });
        Publisher = new EventPublisher(channel);
        Reader = new EventReader(channel);
    }

    public IEventPublisher<T> Publisher { get; }
    public IEventReader<T> Reader { get; }

    private class EventPublisher: IEventPublisher<T>
    {
        private readonly ChannelWriter<T> _writer;

        public EventPublisher(ChannelWriter<T> writer)
            => _writer = writer;

        public Task Publish(T @event, CancellationToken cancellationToken)
            => _writer.WriteAsync(@event, cancellationToken).AsTask();
    }

    private class EventReader : IEventReader<T>
    {
        private readonly ChannelReader<T> _reader;
        private int _lastId;

        public EventReader(ChannelReader<T> reader)
            => _reader = reader;

        public async Task<(T Event, string Id)[]> Read(int batchSize, CancellationToken cancellationToken)
        {
            var read = 0;
            if (!await _reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                return Array.Empty<(T Event, string Id)>();

            var result = new List<(T Event, string Id)>();
            while (_reader.TryRead(out var item)) {
                result.Add((item, _lastId++.ToString(CultureInfo.InvariantCulture)));
                if (++read >= batchSize)
                    break;
            }
            return result.Count > 0
                ? result.ToArray()
                : Array.Empty<(T Event, string Id)>();
        }

        public Task Ack(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}


