using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public class FileUploaderService
{
    private readonly Lock _lock = new ();
    private readonly Dictionary<string, UploadJob> _jobs = new (StringComparer.Ordinal);
    private readonly OperationQueue _operationQueue;
    private readonly Func<string, CancellationToken, Task<UploadId>> _getUploadId;
    private readonly Func<string, CancellationToken, Task<MediaId>> _getMediaId;
    private readonly Func<UploadId, MediaId, CancellationToken, Task> _startProcessing;
    private readonly Func<string, CancellationToken, Task> _clearUploadId;
    private ILogger Log { get; }

    public event Func<string, double, Task>? ProgressChanged;
    public event Func<string, UploadId, MediaId, Task>? Uploaded;
    public event Func<string, Exception, Task>? Failed;
    public event Func<string, Task>? Canceled;

    public FileUploaderService(
        ILogger log,
        Func<string, CancellationToken, Task<UploadId>> getUploadId,
        Func<string, CancellationToken, Task<MediaId>> getMediaId,
        Func<UploadId, MediaId, CancellationToken, Task> startProcessing,
        Func<string, CancellationToken, Task> clearUploadId)
    {
        _getUploadId = getUploadId;
        _getMediaId = getMediaId;
        _startProcessing = startProcessing;
        _clearUploadId = clearUploadId;
        // TODO: add queues with different priorities for small and big files.
        _operationQueue = new OperationQueue();
        Log = log;
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

    private Task<MediaId> GetMediaId(string sessionId, CancellationToken cancellationToken)
        => _getMediaId(sessionId, cancellationToken);

    private Task StartProcessing(UploadId uploadId, MediaId mediaId, CancellationToken cancellationToken)
        => _startProcessing(uploadId, mediaId, cancellationToken);

    private Task ClearUploadId(string sessionId, CancellationToken cancellationToken)
        => _clearUploadId(sessionId, cancellationToken);

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
            var progressTracker = new FileUploadProgressTracker();
            var uploadOperation = new FileUploadOperation(whenFileStreamReady, StartFunc, progressTracker);
            PropagateFileUploadProgress(uploadOperation, Session, Owner);
            return uploadOperation;

            async Task<(UploadId, MediaId)> StartFunc(CancellationToken ct)
            {
                try {
                    return await UploadAndStartProcessing(fileProvider, progressTracker, ct).ConfigureAwait(false);
                }
                catch (UploadNotFoundException) {
                    await Owner.ClearUploadId(Session.SessionId, ct).ConfigureAwait(false);
                    throw;
                }
            }
        }

        private async Task<(UploadId, MediaId)> UploadAndStartProcessing(IFileProvider fileProvider, FileUploadProgressTracker progress, CancellationToken ct)
        {
            var mediaId = await Owner.GetMediaId(Session.SessionId, ct).ConfigureAwait(false);
            mediaId.Require();
            var uploadId = await Owner.GetUploadId(Session.SessionId, ct).ConfigureAwait(false);
            await fileProvider.UploadData(uploadId, progress, ct).ConfigureAwait(false);
            await Owner.StartProcessing(uploadId, mediaId, ct).ConfigureAwait(false);
            return (uploadId, mediaId);
        }

        private void PropagateFileUploadProgress(
            FileUploadOperation uploadOperation,
            UploadSession session,
            FileUploaderService owner)
        {
            var sessionId = session.SessionId;
            var progressTracker = uploadOperation.ProgressTracker;
            var progressChangedThrottler = Throttler.New<double>(
                TimeSpan.FromMilliseconds(500),
                value => {
                    Log.LogDebug("**** Uploading file  '{FileName}' for '{SessionId}' - '{Progress:P}'", session.FileName, sessionId, value / 100.0);
                    _ = owner.ProgressChanged?.Invoke(sessionId, value);
                });
            progressTracker.ProgressChanged += (_, value) => progressChangedThrottler.Throttle(value);
            _ = progressTracker.Task.ContinueWith(async t => {
                if (t.IsCompletedSuccessfully) {
                    Log.LogInformation("**** Uploaded file '{FileName}' for '{SessionId}', processing started", session.FileName, sessionId);
                    var (uploadId, mediaId) = t.GetAwaiter().GetResult();
                    await (owner.Uploaded?.Invoke(sessionId, uploadId, mediaId) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                else if (t.IsFaulted) {
                    foreach (var ex in t.Exception.Flatten().InnerExceptions)
                        Log.LogError(ex, "**** Failed to upload file '{FileName}' for '{SessionId}'('{UploadId}')", session.FileName, sessionId, session.UploadId);
                    await RaiseUploadFailed(t.Exception).ConfigureAwait(false);
                }
                else if (t.IsCanceled) {
                    if (uploadOperation.CancellationToken.IsCancellationRequested) {
                        Log.LogInformation("**** Canceled upload file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                        await (owner.Canceled?.Invoke(sessionId) ?? Task.CompletedTask).ConfigureAwait(false);
                    }
                    else {
                        var ex = new OperationCanceledException("The upload was canceled unexpectedly");
                        Log.LogError(ex, "**** Failed to upload file '{FileName}' for '{SessionId}'('{UploadId}')", session.FileName, sessionId, session.UploadId);
                        await RaiseUploadFailed(ex).ConfigureAwait(false);
                    }
                }
                owner.RemoveJob(this);
                uploadOperation.DisposeSilently();
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
