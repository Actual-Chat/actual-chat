// using Microsoft.Extensions.Hosting;
//
// namespace ActualChat.Events;
//
// public class EventListener<T>: WorkerBase where T: IEvent
// {
//     private readonly IEventReader<T> _eventReader;
//     private readonly Lazy<IEventHandler<T>> _eventHandler;
//     private readonly ICommander _commander;
//     private readonly IHostLifetime _hostLifetime;
//     private readonly ILogger<EventListener<T>> _log;
//
//     public EventListener(
//         IEventReader<T> eventReader,
//         Lazy<IEventHandler<T>> eventHandler,
//         ICommander commander,
//         IHostLifetime hostLifetime,
//         ILogger<EventListener<T>> log)
//     {
//         _eventReader = eventReader;
//         _eventHandler = eventHandler;
//         _commander = commander;
//         _hostLifetime = hostLifetime;
//         _log = log;
//     }
//
//     protected override async Task RunInternal(CancellationToken cancellationToken)
//     {
//         await _hostLifetime.WaitForStartAsync(cancellationToken).ConfigureAwait(false);
//
//         const int batchSize = 10;
//         var ackTasks = new List<Task>(batchSize);
//         while (!cancellationToken.IsCancellationRequested)
//             try {
//                 ackTasks.Clear();
//                 var batch = await _eventReader.Read(batchSize, cancellationToken).ConfigureAwait(false);
//                 if (batch.Length == 0)
//                     continue;
//
//                 foreach (var (@event, id) in batch) {
//                     var task = _eventHandler.Value
//                         .Handle(@event, _commander, cancellationToken)
//                         .ContinueWith(
//                             _ => _eventReader.Ack(id, cancellationToken),
//                             CancellationToken.None,
//                             TaskContinuationOptions.ExecuteSynchronously,
//                             TaskScheduler.Default);
//                     ackTasks.Add(task);
//                 }
//                 await Task.WhenAll(ackTasks).ConfigureAwait(false);
//             }
//             catch (Exception e) when (e is not OperationCanceledException) {
//                 _log.LogWarning(e, "Error processing event");
//             }
//     }
// }
