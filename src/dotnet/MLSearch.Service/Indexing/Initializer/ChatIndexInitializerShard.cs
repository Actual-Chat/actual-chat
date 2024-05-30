namespace ActualChat.MLSearch.Indexing.Initializer;

internal interface IChatIndexInitializerShard
{
    ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion job, CancellationToken cancellationToken);
    Task UseAsync(CancellationToken cancellationToken);
}

internal sealed class ChatIndexInitializerShard(
    IMomentClock clock,
    ICommander commander,
    IInfiniteChatSequence chatSequence,
    ICursorStates<ChatIndexInitializerShard.Cursor> cursorStates,
    ILogger<ChatIndexInitializerShard> log
) : IChatIndexInitializerShard
{
    private object _lock = new();
    internal record Cursor(long LastVersion);
    public const string CursorKey = $"{nameof(ChatIndexInitializer)}.{nameof(Cursor)}";

    private long _maxVersion;
    private long _eventCount;
    private Channel<MLSearch_TriggerChatIndexingCompletion>? _events;
    private Channel<MLSearch_TriggerChatIndexingCompletion> Events => LazyInitializer.EnsureInitialized(
        ref _events,
        ref _lock,
        () => Channel.CreateBounded<MLSearch_TriggerChatIndexingCompletion>(new BoundedChannelOptions(InputBufferCapacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        }));
    private SemaphoreSlim? _semaphore;
    private SemaphoreSlim Semaphore => LazyInitializer.EnsureInitialized(
        ref _semaphore,
        ref _lock,
        () => new(MaxConcurrency, MaxConcurrency));
    private readonly ConcurrentDictionary<ChatId, (long, long)> _scheduledJobs = new();

    public int InputBufferCapacity { get; init; } = 50;
    public int MaxConcurrency { get; init; } = 20;
    public TimeSpan UpdateCursorInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ExecuteJobTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public async ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion evt, CancellationToken cancellationToken)
        => await Events.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);

    public async Task UseAsync(CancellationToken cancellationToken)
    {
        var cursor = await cursorStates.LoadAsync(CursorKey, cancellationToken).ConfigureAwait(false) ?? new(0);
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
            var evt = await Events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _eventCount);
            if (_scheduledJobs.TryRemove(evt.Id, out var info) && info is var (version, _)) {
                if (Volatile.Read(ref _maxVersion) < version) {
                    Volatile.Write(ref _maxVersion, version);
                }
                Semaphore.Release();
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
                        Semaphore.Release();
                    }
                }
                await cursorStates.SaveAsync(CursorKey, new Cursor(nextVersion), cancellationToken).ConfigureAwait(false);
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

        await foreach(var (chatId, version) in chatSequence.LoadAsync(minVersion, cancellationToken).ConfigureAwait(false)) {
            await Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
}
