namespace ActualChat.Events;

public class EventListener<T>: WorkerBase where T: IEvent
{
    private readonly IEventReader<T> _eventReader;
    private readonly IEventHandler<T> _eventHandler;
    private readonly ICommander _commander;
    private readonly ILogger<EventListener<T>> _log;

    public EventListener(
        IEventReader<T> eventReader,
        IEventHandler<T> eventHandler,
        ICommander commander,
        ILogger<EventListener<T>> log)
    {
        _eventReader = eventReader;
        _eventHandler = eventHandler;
        _commander = commander;
        _log = log;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        const int batchSize = 10;
        var ackTasks = new List<Task>(batchSize);
        while (!cancellationToken.IsCancellationRequested)
            try {
                ackTasks.Clear();
                var batch = await _eventReader.Read(batchSize, cancellationToken).ConfigureAwait(false);
                if (batch.Length == 0)
                    continue;

                foreach (var (@event, id) in batch) {
                    var task = _eventHandler
                        .Handle(@event, _commander, cancellationToken)
                        .ContinueWith(
                            _ => _eventReader.Ack(id, cancellationToken),
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    ackTasks.Add(task);
                }
                await Task.WhenAll(ackTasks).ConfigureAwait(false);
            }
            catch (Exception e) {
                _log.LogWarning(e, "Error processing event");
            }
    }
}
