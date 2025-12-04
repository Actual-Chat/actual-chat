using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public class FileUploaderService
{
    private readonly Lock _lock = new ();
    private readonly Dictionary<string, UploadJob> _jobs = new (StringComparer.Ordinal);
    private readonly IServiceProvider _services;
    private readonly OperationQueue _operationQueue;
    private readonly Func<string, CancellationToken, Task<UploadId>> _getUploadId;
    private readonly Func<UploadId, CancellationToken, Task<MediaContent>> _convertUpload;

    private ILogger Log => _services.LogFor(GetType());

    public event Func<string, double, Task>? ProgressChanged;
    public event Func<string, MediaContent, Task>? Completed;
    public event Func<string, Exception, Task>? Failed;
    public event Func<string, Task>? Canceled;

    public FileUploaderService(
        IServiceProvider services,
        Func<string, CancellationToken, Task<UploadId>> getUploadId,
        Func<UploadId, CancellationToken, Task<MediaContent>> convertUpload)
    {
        _services = services;
        _getUploadId = getUploadId;
        _convertUpload = convertUpload;
        // TODO: add queues with different priorities for small and big files.
        _operationQueue = new OperationQueue();
    }

    public async Task StartOrResumeUpload(UploadSession session)
    {
        UploadJob? job;
        lock (_lock) {
            if (!_jobs.TryGetValue(session.SessionId, out job)) {
                job = new UploadJob(this, session);
                _jobs[session.SessionId] = job;
            }
        }
        await job.StartOrResumeUpload().ConfigureAwait(false);
    }

    public async Task<bool> CancelUpload(string sessionId)
    {
        UploadJob? job;
        lock (_lock) {
            if (!_jobs.TryGetValue(sessionId, out job))
                return false;
        }
        await job.Cancel().ConfigureAwait(false);
        return true;
    }

    private void RemoveJob(UploadJob uploadJob)
    {
        lock (_lock) {
            _jobs.Remove(uploadJob.Session.SessionId, out _);
        }
    }

    private void EnqueueFileUploadOperation(FileUploadOperation fileUploadOperation)
        => _operationQueue.Enqueue(fileUploadOperation);

    private Task<UploadId> GetUploadId(string sessionId, CancellationToken cancellationToken)
        => _getUploadId(sessionId, cancellationToken);

    private Task<MediaContent> ConvertUpload(UploadId uploadId, CancellationToken cancellationToken)
        => _convertUpload(uploadId, cancellationToken);

    // Nested types

    private record UploadJob(FileUploaderService Owner, UploadSession Session)
    {
        private readonly AsyncLock _asyncLock = new ();
        private bool _isCancelled;
        private bool _isResumed;
        private FileUploadOperation? _uploadOperation;

        private ILogger Log => Owner.Log;

        public async Task StartOrResumeUpload(CancellationToken cancellationToken = default)
        {
            using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
            releaser.MarkLockedLocally();
            await StartOrResumeUploadInternal().ConfigureAwait(false);
        }

        public async Task Cancel(CancellationToken cancellationToken = default)
        {
            using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
            releaser.MarkLockedLocally();
            await CancelInternal().ConfigureAwait(false);
        }

        private async Task StartOrResumeUploadInternal()
        {
            if (_isCancelled)
                throw new InvalidOperationException("Upload is cancelled");

            if (_isResumed)
                return;

            _isResumed = true;

            try {
                _uploadOperation ??= CreateUploadOperation();
                Log.LogInformation("**** Started uploading file '{FileName}' for '{SessionId}'", Session.FileName, Session.SessionId);
            }
            catch (Exception ex) {
                Log.LogError(ex, "**** Failed to resume uploading file '{FileName}' for '{SessionId}'", Session.FileName, Session.SessionId);
                await RaiseUploadFailed(ex).ConfigureAwait(false);
                return;
            }
            Owner.EnqueueFileUploadOperation(_uploadOperation);
        }

        private Task CancelInternal()
        {
            if (_isCancelled)
                return Task.CompletedTask;

            _isCancelled = true;
            if (!_isResumed)
                return Task.CompletedTask;

            _uploadOperation?.Cancel();
            return Task.CompletedTask;
        }

        private FileUploadOperation CreateUploadOperation()
        {
            var fileProvider = Session.FileProvider;
            var whenFileStreamReady = fileProvider.WhenFileStreamReady();
            var progress = new UploadProgressTracker();
            var uploadOperation = new FileUploadOperation(whenFileStreamReady, StartFunc, progress);
            PropagateFileUploadProgress(uploadOperation.ProgressTracker, Session, Owner);
            return uploadOperation;

            async Task<MediaContent> StartFunc(CancellationToken ct)
            {
                var uploadId = await Owner.GetUploadId(Session.SessionId, ct).ConfigureAwait(false);
                await fileProvider.UploadData(uploadId, progress, ct).ConfigureAwait(false);
                var mediaContent = await Owner.ConvertUpload(uploadId, ct).ConfigureAwait(false);
                return mediaContent;
            }
        }

        private void PropagateFileUploadProgress(
            UploadProgressTracker progressTracker,
            UploadSession session,
            FileUploaderService owner)
        {
            var sessionId = session.SessionId;
            var progressChangedThrottler = Throttler.New<double>(
                TimeSpan.FromMilliseconds(500),
                value => {
                    Log.LogInformation("**** Uploading file  '{FileName}' for '{SessionId}' - '{Progress:P}'", session.FileName, sessionId, value / 100.0);
                    _ = owner.ProgressChanged?.Invoke(sessionId, value);
                });
            progressTracker.ProgressChanged += (_, value) => progressChangedThrottler.Throttle(value);
            _ = progressTracker.Task.ContinueWith(async t => {
                if (t.IsCompletedSuccessfully) {
                    Log.LogInformation("**** Uploaded file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                    var mediaContent = t.Result;
                    await (owner.Completed?.Invoke(sessionId, mediaContent) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                else if (t.IsFaulted) {
                    foreach (var ex in t.Exception.Flatten().InnerExceptions)
                        Log.LogError(ex, "**** Failed to upload file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                    await RaiseUploadFailed(t.Exception).ConfigureAwait(false);
                }
                else if (t.IsCanceled) {
                    Log.LogInformation("**** Canceled upload file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                    await (owner.Canceled?.Invoke(sessionId) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                owner.RemoveJob(this);
            }, TaskScheduler.Default);
        }

        private async Task RaiseUploadFailed(Exception ex)
            => await (Owner.Failed?.Invoke(Session.SessionId, ex) ?? Task.CompletedTask).ConfigureAwait(false);
    }

    private class OperationQueue
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
            var task = operation.ProgressTracker.Task;
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

                var operationsToAwaitToBeReady = new List<Task>();
                foreach (var operation in _operations) {
                    if (activeCount >= MaxActiveCount)
                        break;
                    if (operation.HasStarted)
                        continue;
                    if (!operation.WhenReadyToStart.IsCompleted) {
                        operationsToAwaitToBeReady.Add(operation.WhenReadyToStart);
                        continue;
                    }
                    toStart.Add(operation);
                    activeCount++;
                }
                foreach (var operation in toStart)
                    operation.Start();

                if (operationsToAwaitToBeReady.Count > 0) {
                    _ = Task.WhenAny(operationsToAwaitToBeReady)
                        .ContinueWith(_ => ReviewQueue(), TaskScheduler.Default);
                }
            }
        }
    }
}
