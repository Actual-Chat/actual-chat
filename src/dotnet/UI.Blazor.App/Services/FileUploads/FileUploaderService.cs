using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploaderService
{
    event Func<string, int, int, Task>? OnProgress;
    event Func<string, Task>? OnCompleted;
    event Func<string, string, Task>? OnFailed;

    Task StartOrResumeUpload(UploadSession session);
    Task CancelUpload(string sessionId);
}

public class FileUploaderService(IServiceProvider services) : IFileUploaderService
{
    private readonly Lock _lock = new ();
    private readonly Dictionary<string, UploadJob> _jobs = new (StringComparer.Ordinal);
    private ILogger Log => services.LogFor(GetType());

    public event Func<string, int, int, Task>? OnProgress;
    public event Func<string, Task>? OnCompleted;
    public event Func<string, string, Task>? OnFailed;

    public async Task StartOrResumeUpload(UploadSession session)
    {
        UploadJob? job;
        lock (_lock) {
            if (!_jobs.TryGetValue(session.SessionId, out job)) {
                job = new UploadJob(this, session);
                _jobs[session.SessionId] = job;
            }
        }

        CancellationToken cancellationToken = default;
        using var releaser = await job.Lock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        await job.StartOrResumeUpload().ConfigureAwait(false);
    }

    public async Task CancelUpload(string sessionId)
    {
        UploadJob? job;
        lock (_lock) {
            if (!_jobs.TryGetValue(sessionId, out job))
                return;
        }

        CancellationToken cancellationToken = default;
        using var releaser = await job.Lock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        await job.Cancel().ConfigureAwait(false);
    }

    private record UploadJob(FileUploaderService Owner, UploadSession Session)
    {
        private bool _isCancelled;
        private bool _isResumed;
        private IFileUploadOperation? _uploadOperation;
        public readonly AsyncLock Lock = new ();

        public async Task StartOrResumeUpload()
        {
            if (_isCancelled)
                throw new InvalidOperationException("Upload is cancelled");

            if (_isResumed)
                return;

            _isResumed = true;

            try {
                _uploadOperation ??= await CreateUploadOperation().ConfigureAwait(false);
                Owner.Log.LogInformation("**** Started uploading file '{FileName}' for '{SessionId}'", Session.FileName, Session.SessionId);
            }
            catch (Exception ex) {
                Owner.Log.LogError(ex, "**** Failed to resume uploading file '{FileName}' for '{SessionId}'", Session.FileName, Session.SessionId);
                await (Owner.OnFailed?.Invoke(Session.SessionId, ex.Message) ?? Task.CompletedTask).ConfigureAwait(false);
                return;
            }
            Owner.EnqueueFileUploadOperation(_uploadOperation);
        }

        public Task Cancel()
        {
            if (_isCancelled)
                return Task.CompletedTask;

            _isCancelled = true;
            if (!_isResumed)
                return Task.CompletedTask;

            _uploadOperation?.Cancel();
            return Task.CompletedTask;
        }

        private async Task<IFileUploadOperation> CreateUploadOperation()
        {
            var chatId = Session.ChatId;
            var fileProvider = Session.FileProvider;
            var uploadOperation = await fileProvider.CreateUploadOperation(chatId).ConfigureAwait(false);
            var progressTracker = Session.ProgressTracker;
            StartOperationProgressTracking(Session, uploadOperation, progressTracker, Owner);
            return uploadOperation;
        }

        private static void StartOperationProgressTracking(UploadSession session, IFileUploadOperation uploadOperation, UploadSessionProgressTracker progressTracker, FileUploaderService owner)
        {
            var sessionId = session.SessionId;
            uploadOperation.Progress.ProgressChanged += (_, value) => {
                owner.Log.LogInformation("**** Uploading file '{FileName}' for '{SessionId}' - '{Progress:P}'", session.FileName, sessionId, value / 100.0);
                progressTracker.ReportProgress(value);
            };
            _ = uploadOperation.Task.ContinueWith(async t => {
                if (t.IsCompletedSuccessfully) {
                    owner.Log.LogInformation("**** Uploaded file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                    var mediaContent = t.Result;
                    progressTracker.SetResult(mediaContent);
                    await (owner.OnCompleted?.Invoke(sessionId) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                else if (t.IsFaulted) {
                    foreach (var ex in t.Exception.Flatten().InnerExceptions)
                        owner.Log.LogError(ex, "**** Failed to upload file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                    progressTracker.SetException(t.Exception);
                    await (owner.OnFailed?.Invoke(sessionId, t.Exception.Message) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                else if (t.IsCanceled) {
                    owner.Log.LogInformation("**** Canceled upload file '{FileName}' for '{SessionId}'", session.FileName, sessionId);
                    progressTracker.SetCanceled();
                    // Owner.OnFailed?.Invoke(Session.SessionId, "Canceled");
                }
            }, TaskScheduler.Default);
        }
    }

    private void EnqueueFileUploadOperation(IFileUploadOperation fileUploadOperation)
        => services.GetRequiredService<FileUploadQueue>().Enqueue(fileUploadOperation);
}
