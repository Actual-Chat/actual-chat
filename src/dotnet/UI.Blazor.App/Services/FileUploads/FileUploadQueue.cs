namespace ActualChat.UI.Blazor.App.Services;

public class FileUploadOperation(Func<Task> startFunc)
{
    private readonly TaskCompletionSource _tcs = TaskCompletionSourceExt.New();
    private long _state;

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
        lock (_lock) {
            int activeCount = 0;
            var toStart = new List<FileUploadOperation>();
            foreach (var operation in _operations)
                if (operation.HasStarted)
                    activeCount++;

            foreach (var operation in _operations) {
                if (activeCount >= MaxActiveCount)
                    break;
                if (operation.HasStarted)
                    continue;
                toStart.Add(operation);
                activeCount++;
            }
            foreach (var operation in toStart)
                operation.Start();
        }
    }
}
