using ActualChat.Chat;
using ActualChat.MLSearch.Engine.Indexing;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexInitializerShard
{
    ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion job, CancellationToken cancellationToken);
    Task UseAsync(CancellationToken cancellationToken);
}

internal class ChatIndexInitializerShard(
    IMomentClock clock,
    ICommander commander,
    IChatsBackend chats,
    ICursorStates<ChatIndexInitializerShard.Cursor> cursorStates,
    ILogger<ChatIndexInitializerShard> log
) : IChatIndexInitializerShard
{
    internal record Cursor(long LastVersion);
    private const string CursorKey = $"{nameof(ChatIndexInitializer)}.{nameof(Cursor)}";
    private const int BatchSize = 1000;
    private static readonly TimeSpan UpdateCursorInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoChatsIdleInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExecuteJobTimeout = TimeSpan.FromMinutes(3);
    private readonly Channel<MLSearch_TriggerChatIndexingCompletion> _events =
        Channel.CreateBounded<MLSearch_TriggerChatIndexingCompletion>(new BoundedChannelOptions(BatchSize) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private long _eventCount;
    private long _maxVersion;
    private readonly SemaphoreSlim _semaphore = new(BatchSize, BatchSize);
    private readonly ConcurrentDictionary<ChatId, (long, long)> _scheduledJobs = new();

    public async ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion evt, CancellationToken cancellationToken)
        => await _events.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);

    public async Task UseAsync(CancellationToken cancellationToken)
    {
        var cursor = await cursorStates.Load(CursorKey, cancellationToken).ConfigureAwait(false) ?? new(0);
        _maxVersion = cursor.LastVersion;
        await Task.WhenAll([
            ScheduleJobsAsync(cursor.LastVersion, cancellationToken),
            HandleEventsAsync(cancellationToken),
            UpdateCursorAsync(cancellationToken),
        ]).ConfigureAwait(false);
    }

    private async Task HandleEventsAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested) {
            var evt = await _events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _eventCount);
            if (_scheduledJobs.TryRemove(evt.Id, out var info) && info is var (version, _)) {
                if (Volatile.Read(ref _maxVersion) < version) {
                    Volatile.Write(ref _maxVersion, version);
                }
                _semaphore.Release();
            }
        }
    }

    private async Task UpdateCursorAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        var lastEventCount = Volatile.Read(ref _eventCount);
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await clock.Delay(UpdateCursorInterval, cancellationToken).ConfigureAwait(false);
                var eventCount = Volatile.Read(ref _eventCount);
                if (eventCount == lastEventCount) {
                    // There is no point in advancing cursor as no job reported its completion.
                    continue;
                }
                lastEventCount = eventCount;
                var now = clock.Now.EpochOffset.Ticks;
                var pastMoment = now - ExecuteJobTimeout.Ticks;
                var stallJobs = new List<ChatId>();
                // This is max chat version where indexing is completed.
                // In the case our schedule is empty we may want to advance cursor till there.
                var nextVersion = Volatile.Read(ref _maxVersion) + 1;
                foreach (var (jobId, (version, timestamp)) in _scheduledJobs) {
                    if (timestamp < pastMoment) {
                        // Job is stall, so skipping its version.
                        stallJobs.Add(jobId);
                    }
                    else {
                        // Job is still in the scheduled state,
                        // so we may want to re-run it in the case of failure.
                        nextVersion = Math.Min(nextVersion, version);
                    }
                }
                foreach (var jobId in stallJobs) {
                    if (_scheduledJobs.TryRemove(jobId, out var info) && info is var (_, timestamp)) {
                        log.LogInformation("Evicting indexing job for chat #{JobId} which is stall for {Interval}.",
                            jobId, TimeSpan.FromTicks(now - timestamp));
                        _semaphore.Release();
                    }
                }
                await cursorStates.Save(CursorKey, new Cursor(nextVersion), cancellationToken).ConfigureAwait(false);
                log.LogInformation("Indexing cursor is advanced to the chat version #{Version}", nextVersion);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                log.LogError(e, "Failed to update cursor state.");
            }
        }
    }

    private async Task ScheduleJobsAsync(long minVersion, CancellationToken cancellationToken)
    {
        await Task.Yield();

        await foreach(var (chatId, version) in EnumerateChatsAsync(minVersion, cancellationToken).ConfigureAwait(false)) {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            while(true) {
                try {
                    var job = new MLSearch_TriggerChatIndexing(chatId);
                    await commander.Call(job, cancellationToken).ConfigureAwait(false);
                    _scheduledJobs[chatId] = (version, clock.Now.EpochOffset.Ticks);
                    break;
                }
                catch(Exception e) when (e is not OperationCanceledException) {
                    log.LogError(e, "Failed to schedule an indexing job for chat #{ChatId}.", chatId);
                    await clock.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async IAsyncEnumerable<(ChatId, long)> EnumerateChatsAsync(
        long minVersion, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested) {
            ApiArray<Chat.Chat> batch;
            try {
                // TODO: handle the case with infinite getting the same chats in a batch
                // when there are more than BatchSize chats with the same version
                batch = await chats
                    .ListChanged(minVersion, long.MaxValue, ChatId.None, BatchSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch(Exception e) when (e is not OperationCanceledException) {
                log.LogError(e,
                    "Failed to load a batch of chats of length {Len} in the version range from {MinVersion} to infinity.",
                    BatchSize, minVersion);
                await clock.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (batch.Count==0) {
                await Task.Delay(NoChatsIdleInterval, cancellationToken).ConfigureAwait(false);
            }
            else {
                foreach (var chat in batch) {
                    cancellationToken.ThrowIfCancellationRequested();
                    minVersion = chat.Version + 1;
                    yield return (chat.Id, chat.Version);
                }
            }
        }
    }
}
