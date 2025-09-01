namespace ActualChat.UI.Blazor.App.Services;

public class FileUploadQueue
{
    private readonly Lock _lock = new();
    private readonly List<IFileUploadOperation> _operations = new ();

    public void Enqueue(IFileUploadOperation operation)
    {
        lock (_lock) {
            _operations.Add(operation);
            TrackOperation(operation);
        }
        CheckUploadsOperations();
    }

    private void TrackOperation(IFileUploadOperation operation)
    {
        _ = operation.Task.ContinueWith(_ => OnOperationCompleted(operation), TaskScheduler.Default);
        if (operation.Task.IsCompleted)
            OnOperationCompleted(operation);
    }

    private void OnOperationCompleted(IFileUploadOperation operation)
    {
        lock (_lock)
            _operations.Remove(operation);
        CheckUploadsOperations();
    }

    private void CheckUploadsOperations()
    {
        lock (_lock) {
            int activeCount = 0;
            var toStart = new List<IFileUploadOperation>();
            foreach (var operation in _operations) {
                if (operation.HasStarted)
                    activeCount++;
                else if (activeCount < 2) {
                    toStart.Add(operation);
                    activeCount++;
                }
            }
            foreach (var operation in toStart)
                operation.Start();
        }
    }
}
