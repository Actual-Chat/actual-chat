using ActualLab.Resilience;

namespace ActualChat.MLSearch.Indexing.Initializer;

internal interface IChatIndexInitializerShard
{
    ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion job, CancellationToken cancellationToken = default);
    Task UseAsync(CancellationToken cancellationToken = default);
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

    // # Helper types
    public record Cursor(long LastVersion);
    public class SharedState(Cursor cursor, int maxConcurrency)
    {
        private long _maxVersion = cursor.LastVersion;
        public ref long MaxVersion => ref _maxVersion;
        private long _eventCount;
        public ref long EventCount => ref _eventCount;
        public long PrevEventCount { get; set; }

        public SemaphoreSlim Semaphore { get; } = new(maxConcurrency, maxConcurrency);
        public ConcurrentDictionary<ChatId, (long, Moment)> ScheduledJobs { get; } = new();
    }
    public record ChatInfo(ChatId ChatId, long Version)
    {
        public ChatInfo((ChatId ChatId, long Version) info): this(info.ChatId, info.Version)
        { }
    }
    public record RetrySettings(int AttemptCount, RetryDelaySeq RetryDelaySeq, TransiencyResolver TransiencyResolver);

    // # Delegates
    public delegate ValueTask UpdateCursorHandler(
        Moment moment, SharedState state, TimeSpan stallJobTimeout,
        ICursorStates<Cursor> cursorStates,
        ILogger log,
        CancellationToken cancellationToken);
    public delegate ValueTask ScheduleJobHandler(
        ChatInfo chatInfo, SharedState state, RetrySettings retrySettings,
        ICommander commander,
        IMomentClock clock,
        ILogger log,
        CancellationToken cancellationToken);
    public delegate void CompleteJobHandler(
        MLSearch_TriggerChatIndexingCompletion evt, SharedState state);

    // # Fields
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

    // # Configuration properties
    public int InputBufferCapacity { get; init; } = 50;
    public int MaxConcurrency { get; init; } = 20;
    public TimeSpan UpdateCursorInterval { get; init; } = TimeSpan.FromSeconds(30);
    public RetrySettings ScheduleJobRetrySettings { get; init; } =
        new (10, RetryDelaySeq.Exp(0.2, 10), TransiencyResolvers.PreferTransient);
    public TimeSpan ExecuteJobTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public ScheduleJobHandler OnScheduleJob { get; init; } = ScheduleIndexingJobAsync;
    public CompleteJobHandler OnCompleteJob { get; init; } = HandleCompletionEvent;
    public UpdateCursorHandler OnUpdateCursor { get; init; } = UpdateCursorAsync;

    // # Public API methods
    public async ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion evt, CancellationToken cancellationToken = default)
        => await Events.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);

    public async Task UseAsync(CancellationToken cancellationToken = default)
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

    // # Implementation
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

    // ## Schedule jobs
    private ValueTask ScheduleJobForChatAsync(ChatInfo chatInfo, SharedState state, CancellationToken cancellationToken)
        => OnScheduleJob(chatInfo, state, ScheduleJobRetrySettings, commander, clock, log, cancellationToken);

    public static async ValueTask ScheduleIndexingJobAsync(
        ChatInfo chatInfo,
        SharedState state,
        RetrySettings retrySettings,
        ICommander commander,
        IMomentClock clock,
        ILogger log,
        CancellationToken cancellationToken)
    {
        await state.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        var (chatId, version) = chatInfo;

        await AsyncChain.From(ct => ScheduleChatIndexing(chatId, commander, ct))
                .WithTransiencyResolver(retrySettings.TransiencyResolver)
                .Log(LogLevel.Debug, log)
                .Retry(retrySettings.RetryDelaySeq, retrySettings.AttemptCount, clock, log)
                .Run(cancellationToken)
            .ConfigureAwait(false);

        state.ScheduledJobs[chatId] = (version, clock.Now);

        return;

        static async Task ScheduleChatIndexing(ChatId chatId, ICommander commander, CancellationToken cancellationToken)
        {
            var job = new MLSearch_TriggerChatIndexing(chatId);
            await commander.Call(job, cancellationToken).ConfigureAwait(false);
        }
    }

    // ## Handle job completion events
    private async IAsyncEnumerable<MLSearch_TriggerChatIndexingCompletion> ReadCompletionEvents(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await Events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        // ReSharper disable once IteratorNeverReturns
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

    // ## Update cursor
    private async IAsyncEnumerable<Moment> ReadUpdateCursorMoments(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            await clock.Delay(UpdateCursorInterval, cancellationToken).ConfigureAwait(false);
            yield return clock.Now;
        }
        // ReSharper disable once IteratorNeverReturns
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
            var pastMoment = updateMoment - stallJobTimeout;
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
                        jobId, updateMoment - timestamp);
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
}
