using ActualChat.UI.Services;
using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

public enum UploadSessionState
{
    Created,
    Initializing,
    ClientProcessing,
    Uploading,
    ServerProcessing,
    Completed,
    Cancelled
}

public class UploadSession
{
    private static readonly ILogger Log = StaticLog.For<UploadSession>();

    public event EventHandler<double>? UploadProgressChanged;
    public event EventHandler<double>? ServerProcessingProgressChanged;

    private readonly Func<UploadSessionSnapshot, bool, CancellationToken, Task>? _storage;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly TaskCompletionSource<MediaId> _whenMediaIdReserved = TaskCompletionSourceExt.New<MediaId>();
    private volatile CancellationTokenSource? _cts;
    private UploadSessionSnapshot _snapshot;
    private int _isRunning;
    private Task _runTask = Task.CompletedTask;
    private readonly UploadOperations _uploadOperations;

    public static UploadSessionSnapshot NewUploadSnapshot(IFileProvider fileProvider, MetadataBag metadata,
        Moment now, string mediaScope)
    {
        var snapshot = new UploadSessionSnapshot {
            SessionId = Guid.NewGuid().ToString(),
            FileProvider = fileProvider,
            Metadata = metadata,
            CurrentState = UploadSessionState.Created,
            DataVersion = 1,
            CreatedAt = now,
            LastUpdatedAt = now,
            MediaScope = mediaScope,
        };
        return snapshot;
    }

    public UploadSession(UploadSessionSnapshot snapshot,
        UploadOperations uploadOperations,
        Func<UploadSessionSnapshot, bool, CancellationToken, Task>? storage = null)
    {
        _uploadOperations = uploadOperations;
        _storage = storage;
        _snapshot = snapshot;
        if (snapshot.ReservedMediaId is not null)
            _whenMediaIdReserved.TrySetResult(snapshot.ReservedMediaId);
    }

    public string SessionId => _snapshot.SessionId;
    public IFileProvider FileProvider => _snapshot.FileProvider;
    public string FileName => FileProvider.Metadata.FileName;
    public MediaId? MediaId => _snapshot.ReservedMediaId;
    public UploadId? UploadId => _snapshot.UploadId;
    public UploadSessionState CurrentState => _snapshot.CurrentState;
    public bool IsFailed => _snapshot.IsFailed;
    public Exception? LastError { get; private set; }
    [MemberNotNullWhen(true, nameof(MediaRef))]
    public bool IsCompleted => CurrentState == UploadSessionState.Completed;
    public MediaRef? MediaRef => _snapshot.MediaRef;
    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;
    public bool IsTerminated => CurrentState == UploadSessionState.Completed || CurrentState == UploadSessionState.Cancelled;
    public string? TranscodedFilePath => _snapshot.TranscodedFilePath;
    public Task<MediaId> WhenMediaReserved => _whenMediaIdReserved.Task;

    public bool Resume()
    {
        if (CurrentState == UploadSessionState.Cancelled)
            throw new InvalidOperationException("Upload session is cancelled. Can't resume.");

        if (CurrentState == UploadSessionState.Completed)
            return false;

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return false;

        _cts = new CancellationTokenSource();
        var cancellationToken = _cts.Token;
        _runTask = BackgroundTask.Run(() => RunInternal(cancellationToken), CancellationToken.None);
        _ = _runTask.ContinueWith(_ => {
            _cts.CancelAndDisposeSilently();
            _cts = null;
            Interlocked.Exchange(ref _isRunning, 0);
        }, TaskScheduler.Default);
        return true;
    }

    public async Task Cancel()
    {
        var cts = _cts;
        if (cts != null)
            await cts.CancelAsync().ConfigureAwait(false);
        await _runTask.ConfigureAwait(false);
    }

    private async Task RunInternal(CancellationToken cancellationToken)
    {
        await UpdateState(s => s with { IsFailed = false }, cancellationToken: cancellationToken).ConfigureAwait(false);
        LastError = null;

        if (CurrentState == UploadSessionState.Created)
            await TransitionTo(UploadSessionState.Initializing).ConfigureAwait(false);

        await RunSteps(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunSteps(CancellationToken cancellationToken)
    {
        while (!IsTerminated && !IsFailed) {
            switch (CurrentState) {
                case UploadSessionState.Initializing:
                    await InitializeUpload(cancellationToken).ConfigureAwait(false);
                    break;
                case UploadSessionState.ClientProcessing:
                    await RunClientProcessing(cancellationToken).ConfigureAwait(false);
                    break;
                case UploadSessionState.Uploading:
                    await UploadData(cancellationToken).ConfigureAwait(false);
                    break;
                case UploadSessionState.ServerProcessing:
                    await RunServerProcessing(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return;
            }
        }
    }

    private Task InitializeUpload(CancellationToken cancellationToken) => ExecuteStep(async () => {
        if (_snapshot.ReservedMediaId != null)
            return; // Already initialized
        var mediaId = await _uploadOperations.ReserveMediaId(_snapshot, cancellationToken).ConfigureAwait(false);
        await UpdateState(s => s with {
            ReservedMediaId = mediaId
        }, save: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        _whenMediaIdReserved.TrySetResult(mediaId);
        await TransitionTo(UploadSessionState.ClientProcessing).ConfigureAwait(false);
    }, cancellationToken);

    private Task RunClientProcessing(CancellationToken cancellationToken) => ExecuteStep(async () => {
        var fileProvider = _snapshot.FileProvider;
        var mimeType = fileProvider.Metadata.FileType;
        var filePath = (fileProvider as MauiFileProvider)?.FileRef ?? FilePath.Empty;

        // Wait for the file to be fully loaded before transcoding
        await fileProvider.WhenFileStreamReady().WaitAsync(cancellationToken).ConfigureAwait(false);

        var progress = new Progress<double>(p => {
            _ = UpdateState(s => {
                if (s.CurrentState != UploadSessionState.ClientProcessing)
                    return s;
                return s with { StageProgress = p };
            }, cancellationToken: cancellationToken);
        });

        var transcodedPath = await _uploadOperations.VideoTranscoder
            .Transcode(filePath, mimeType, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!transcodedPath.IsEmpty)
            await UpdateState(s => s with { TranscodedFilePath = transcodedPath.Value },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        await TransitionTo(UploadSessionState.Uploading).ConfigureAwait(false);
    }, cancellationToken);

    private Task UploadData(CancellationToken cancellationToken) => ExecuteStep(async () =>
    {
        using var progress = new ThrottledProgress<double>(p => {
            _ = UpdateState(s => {
                if (s.CurrentState != UploadSessionState.Uploading)
                    return s;
                return s with { StageProgress = p };
            }, save: false, cancellationToken: cancellationToken);
            UploadProgressChanged?.Invoke(this, p);
        }, TimeSpan.FromMilliseconds(250));
        var snapshotAccessor = new UploadSessionSnapshotAccessor(
            () => _snapshot,
            (update, ct) => UpdateState(update, cancellationToken: ct));

        // Use transcoded source if available, otherwise get from the file provider
        var uploadSource = GetTranscodedSource() ?? _snapshot.FileProvider.GetUploadSource();

        await _uploadOperations.UploadData(
            uploadSource,
            snapshotAccessor,
            progress,
            cancellationToken).ConfigureAwait(false);
        await TransitionTo(UploadSessionState.ServerProcessing).ConfigureAwait(false);
    }, cancellationToken);

    private Task RunServerProcessing(CancellationToken cancellationToken) => ExecuteStep(async () => {
        // Start processing (idempotent - safe to call multiple times)
        await _uploadOperations.StartServerProcessing(_snapshot, cancellationToken).ConfigureAwait(false);

        // Wait for completion
        var progress = new Progress<double>(p => {
            _ = UpdateState(s => {
                if (s.CurrentState != UploadSessionState.ServerProcessing)
                    return s;
                return s with { StageProgress = p };
            }, cancellationToken: cancellationToken);
            ServerProcessingProgressChanged?.Invoke(this, p);
        });
        var result = await _uploadOperations.WaitForProcessingCompletion(_snapshot, progress, cancellationToken).ConfigureAwait(false);

        await UpdateState(s => s with { MediaRef = result },
            save: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        await TransitionTo(UploadSessionState.Completed).ConfigureAwait(false);
    }, cancellationToken);

    private async Task ExecuteStep(Func<Task> step, CancellationToken cancellationToken)
    {
        try {
            await step().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            await TransitionTo(UploadSessionState.Cancelled).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await OnFailed(ex, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TransitionTo(UploadSessionState newState)
        => await UpdateState(s => s with { CurrentState = newState, StageProgress = 0 }).ConfigureAwait(false);

    private async Task OnFailed(Exception ex, CancellationToken cancellationToken = default)
    {
        Log.LogError(ex, "Upload '{SessionId}' session for file '{FileName}' failed on step '{Step}'",
            SessionId, FileProvider.Metadata.FileName, _snapshot.CurrentState.ToString());
        LastError = ex;
        await UpdateState(s => s with { IsFailed = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateState(
        Func<UploadSessionSnapshot, UploadSessionSnapshot> update,
        bool save = true,
        CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var temp = _snapshot;
            _snapshot = update(_snapshot);
            if (ReferenceEquals(temp, _snapshot))
                return;

            _snapshot = _snapshot with { LastUpdatedAt = _uploadOperations.Now() };
            if (_storage != null)
                await _storage.Invoke(_snapshot, save, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _stateLock.Release();
        }
    }

    private UploadSource? GetTranscodedSource()
    {
        if (_snapshot.TranscodedFilePath is not { } transcodedPath)
            return null;

        if (!File.Exists(transcodedPath)) {
            Log.LogError("'{SessionId}': transcoded file not found at '{Path}'", SessionId, transcodedPath);
            throw StandardError.Internal("Transcoded video not found");
        }

        var fileInfo = new FileInfo(transcodedPath);
        Log.LogInformation("'{SessionId}': uploading transcoded file '{Path}', size={Size}",
            SessionId, transcodedPath, fileInfo.Length);
        var metadata = new UploadSourceMetadata(MediaMimeTypes.GetMimeType(transcodedPath), fileInfo.Length, transcodedPath.FileName);
        return new UploadSource(metadata, new StreamUploadSource(() => Task.FromResult<Stream>(File.OpenRead(transcodedPath))));
    }
}

public readonly struct UploadSessionSnapshotAccessor(
    Func<UploadSessionSnapshot> getSnapshot,
    Func<Func<UploadSessionSnapshot, UploadSessionSnapshot>, CancellationToken, Task> updateSnapshot)
{
    public UploadSessionSnapshot Get()
        => getSnapshot();

    public Task Update(
        Func<UploadSessionSnapshot, UploadSessionSnapshot> update,
        CancellationToken cancellationToken = default)
        => updateSnapshot(update, cancellationToken);
}
