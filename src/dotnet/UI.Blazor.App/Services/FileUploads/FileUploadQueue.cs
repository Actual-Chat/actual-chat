namespace ActualChat.UI.Blazor.App.Services;

public sealed class FileUploadOperation(long length, Func<Task> startFunc)
{
    private readonly TaskCompletionSource _tcs = TaskCompletionSourceExt.New();
    private long _state;

    public long Length { get; } = length;
    public bool IsLarge => Length >= FileUploadQueue.LargeFileLength;
    public bool HasStarted => Interlocked.Read(ref _state) != 0;
    public Task Task => _tcs.Task;

    public void Start()
     {
         if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
             throw new InvalidOperationException("Already started or cancelled.");

         try {
             _ = startFunc()
                 .ContinueWith(t => {
                         if (t.IsCanceled)
                             _tcs.TrySetCanceled();
                         else if (t.IsFaulted)
                             _tcs.TrySetException(t.Exception);
                         else
                             _tcs.TrySetResult();
                     },
                     TaskScheduler.Default);
         }
         catch (Exception ex) {
             _tcs.TrySetException(ex);
         }
     }
}

public class FileUploadQueue
{
    public const long LargeFileLength = 10 * 1024 * 1024;
    private const int MaxActiveCount = 2;
    private readonly Lock _lock = new();
    private readonly List<FileUploadOperation> _operations = new ();

    public void Enqueue(FileUploadOperation operation)
    {
        lock (_lock) {
            _operations.Add(operation);
            TrackOperation(operation);
        }
        ReviewQueue();
    }

    private void TrackOperation(FileUploadOperation operation)
    {
        var task = operation.Task;
        _ = task.ContinueWith(_ => OnOperationCompleted(operation), TaskScheduler.Default);
        if (task.IsCompleted)
            OnOperationCompleted(operation);
    }

    private void OnOperationCompleted(FileUploadOperation operation)
    {
        lock (_lock)
            _operations.Remove(operation);
        ReviewQueue();
    }

    private void ReviewQueue()
    {
        // Large files split the uplink between them, so they go one at a time, while a small one may run beside
        // a large one instead of waiting for it
        lock (_lock) {
            var activeCount = 0;
            var hasActiveLarge = false;
            var toStart = new List<FileUploadOperation>();
            foreach (var operation in _operations)
                if (operation.HasStarted) {
                    activeCount++;
                    hasActiveLarge |= operation.IsLarge;
                }

            foreach (var operation in _operations) {
                if (activeCount >= MaxActiveCount)
                    break;
                if (operation.HasStarted || (hasActiveLarge && operation.IsLarge))
                    continue;

                toStart.Add(operation);
                activeCount++;
                hasActiveLarge |= operation.IsLarge;
            }
            foreach (var operation in toStart)
                operation.Start();
        }
    }
}
