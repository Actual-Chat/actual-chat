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
    public const string CursorKey = $"{nameof(ChatIndexInitializer)}.{nameof(Cursor)}";
    internal record Cursor(long LastVersion);
    public class SharedState(Cursor cursor, int maxConcurrency)
    {
        private long _maxVersion = cursor.LastVersion;
        public ref long MaxVersion => ref _maxVersion;
        private long _eventCount;
        public ref long EventCount => ref _eventCount;
        public long PrevEventCount { get; set; }

        public SemaphoreSlim Semaphore { get; } = new(maxConcurrency, maxConcurrency);
        public ConcurrentDictionary<ChatId, (long, long)> ScheduledJobs { get; } = new();
    }
    public delegate ValueTask UpdateCursorHandler(
        Moment moment, SharedState state, TimeSpan stallJobTimeout,
        ICursorStates<Cursor> cursorStates,
        ILogger log,
        CancellationToken cancellationToken);

    public delegate ValueTask ScheduleJobHandler(
        ChatInfo chatInfo, SharedState state, TimeSpan retryInterval,
        ICommander commander,
        IMomentClock clock,
        ILogger log,
        CancellationToken cancellationToken);
    public delegate void CompleteJobHandler(
        MLSearch_TriggerChatIndexingCompletion evt, SharedState state);

    private object _lock = new();
    private Channel<MLSearch_TriggerChatIndexingCompletion>? _events;
    private Channel<MLSearch_TriggerChatIndexingCompletion> Events => LazyInitializer.EnsureInitialized(
        ref _events,
        ref _lock,
        () => Channel.CreateBounded<MLSearch_TriggerChatIndexingCompletion>(new BoundedChannelOptions(InputBufferCapacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        }));

    public int InputBufferCapacity { get; init; } = 50;
    public int MaxConcurrency { get; init; } = 20;
    public TimeSpan UpdateCursorInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ExecuteJobTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public UpdateCursorHandler OnUpdateCursor { get; init; } = UpdateCursorAsync;
    public ScheduleJobHandler OnScheduleJob { get; init; } = ScheduleJobForChatAsync;
    public CompleteJobHandler OnCompleteJob { get; init; } = HandleCompletionEvent;

    public async ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion evt, CancellationToken cancellationToken)
        => await Events.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);

    public async Task UseAsync(CancellationToken cancellationToken)
    {
        var cursor = await cursorStates.LoadAsync(CursorKey, cancellationToken).ConfigureAwait(false) ?? new(0);
        var state = new SharedState(cursor, MaxConcurrency);

        var chats = chatSequence
            .LoadAsync(cursor.LastVersion, cancellationToken)
            .Select(info => new ChatInfo(info));
        var completionEvents = ReadCompletionEvents(cancellationToken);
        var updateCursorMoments = ReadUpdateCursorMoments(cancellationToken);

        await Task.WhenAll([
            RunAsync(chats, ScheduleJobForChatAsync, state, cancellationToken),
            RunAsync(completionEvents, HandleCompletionEventAsync, state, cancellationToken),
            RunAsync(updateCursorMoments, UpdateCursorAsync, state, cancellationToken),
        ]).ConfigureAwait(false);
    }

    private ValueTask HandleCompletionEventAsync(MLSearch_TriggerChatIndexingCompletion evt, SharedState state, CancellationToken _) {
        OnCompleteJob(evt, state);
        return ValueTask.CompletedTask;
    }
    public static void HandleCompletionEvent(
        MLSearch_TriggerChatIndexingCompletion evt, SharedState state)
    {
        Interlocked.Increment(ref state.EventCount);
        if (state.ScheduledJobs.TryRemove(evt.Id, out var info) && info is var (version, _)) {
            if (Volatile.Read(ref state.MaxVersion) < version) {
                Volatile.Write(ref state.MaxVersion, version);
            }
            state.Semaphore.Release();
        }
    }

    private async IAsyncEnumerable<MLSearch_TriggerChatIndexingCompletion> ReadCompletionEvents(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await Events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<Moment> ReadUpdateCursorMoments(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            await clock.Delay(UpdateCursorInterval, cancellationToken).ConfigureAwait(false);
            yield return clock.Now;
        }
    }

    private ValueTask UpdateCursorAsync(Moment updateMoment, SharedState state, CancellationToken cancellationToken)
        => OnUpdateCursor(updateMoment, state, UpdateCursorInterval, cursorStates, log, cancellationToken);

    public static async ValueTask UpdateCursorAsync(
        Moment updateMoment,
        SharedState state,
        TimeSpan stallJobTimeout,
        ICursorStates<Cursor> cursorStates,
        ILogger log,
        CancellationToken cancellationToken)
    {
        try {
            var eventCount = Volatile.Read(ref state.EventCount);
            if (eventCount == state.PrevEventCount) {
                // There is no point in advancing cursor as no job reported its completion.
                return;
            }
            state.PrevEventCount = eventCount;
            var now = updateMoment.EpochOffset.Ticks;
            var pastMoment = now - stallJobTimeout.Ticks;
            var stallJobs = new List<ChatId>();
            // This is max chat version where indexing is completed.
            // In the case our schedule is empty we may want to advance cursor till there.
            var nextVersion = Volatile.Read(ref state.MaxVersion) + 1;
            foreach (var (jobId, (version, timestamp)) in state.ScheduledJobs) {
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
                if (state.ScheduledJobs.TryRemove(jobId, out var info) && info is var (_, timestamp)) {
                    log.LogInformation("Evicting indexing job for chat #{JobId} which is stall for {Interval}.",
                        jobId, TimeSpan.FromTicks(now - timestamp));
                    state.Semaphore.Release();
                }
            }
            await cursorStates.SaveAsync(CursorKey, new Cursor(nextVersion), cancellationToken).ConfigureAwait(false);
            log.LogInformation("Indexing cursor is advanced to the chat version #{Version}", nextVersion);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            log.LogError(e, "Failed to update cursor state.");
        }
    }

    public record ChatInfo(ChatId chatId, long version)
    {
        public ChatInfo((ChatId ChatId, long Version) info): this(info.ChatId, info.Version)
        { }
    }

    private ValueTask ScheduleJobForChatAsync(ChatInfo chatInfo, SharedState state, CancellationToken cancellationToken)
        => OnScheduleJob(chatInfo, state, RetryInterval, commander, clock, log, cancellationToken);

    private static async ValueTask ScheduleJobForChatAsync(
        ChatInfo chatInfo,
        SharedState state,
        TimeSpan retryInterval,
        ICommander commander,
        IMomentClock clock,
        ILogger log,
        CancellationToken cancellationToken)
    {
        var (chatId, version) = chatInfo;
        await state.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        while (true) {
            try {
                var job = new MLSearch_TriggerChatIndexing(chatId);
                await commander.Call(job, cancellationToken).ConfigureAwait(false);
                state.ScheduledJobs[chatId] = (version, clock.Now.EpochOffset.Ticks);
                break;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                log.LogError(e, "Failed to schedule an indexing job for chat #{ChatId}.", chatId);
                await clock.Delay(retryInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task RunAsync<TEvent>(
        IAsyncEnumerable<TEvent> events,
        Func<TEvent, SharedState, CancellationToken, ValueTask> handler,
        SharedState state,
        CancellationToken cancellationToken
    )
    {
        await Task.Yield();

        await foreach(var evt in events.ConfigureAwait(false).WithCancellation(cancellationToken)) {
            await handler.Invoke(evt, state, cancellationToken).ConfigureAwait(false);
        }
    }
}
