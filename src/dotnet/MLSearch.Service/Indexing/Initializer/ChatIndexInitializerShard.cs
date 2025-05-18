using ActualChat.MLSearch.Diagnostics;
using ActualChat.Queues;
using ActualLab.Resilience;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.MLSearch.Indexing.Initializer;

internal interface IChatIndexInitializerShard
{
    ValueTask PostAsync(MLSearch_SignalChatIndexingContinuation job, CancellationToken cancellationToken = default);
    ValueTask PostAsync(MLSearch_SignalChatIndexingCompletion job, CancellationToken cancellationToken = default);
    Task UseAsync(CancellationToken cancellationToken = default);
}

internal sealed class ChatIndexInitializerShard(
    MomentClock clock,
    IQueues queues,
    IInfiniteChatSequence chatSequence,
    ICursorStates<ChatIndexInitializerShard.Cursor> cursorStates,
    ILogger<ChatIndexInitializerShard> log
) : IChatIndexInitializerShard
{
    public const string CursorKey = $"{nameof(ChatIndexInitializer)}.{nameof(Cursor)}";
    private const string ClassName = nameof(ChatIndexInitializerShard);
    private const string PostChatIndexingContinuationActivityName = $"{nameof(PostAsync)}({nameof(MLSearch_SignalChatIndexingContinuation)})@{ClassName}";
    private const string PostChatIndexingCompletionActivityName = $"{nameof(PostAsync)}({nameof(MLSearch_SignalChatIndexingCompletion)})@{ClassName}";
    private const string OnScheduleJobActivityName = $"{nameof(ScheduleIndexingJobAsync)}@{ClassName}";
    private const string OnHandleJobEventActivityName = $"{nameof(HandleJobEventAsync)}@{ClassName}";
    private const string OnUpdateCursorActivityName = $"{nameof(UpdateCursorAsync)}@{ClassName}";
    private static readonly ActivitySource ActivitySource = MLSearchInstruments.ActivitySource;

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
    public static class UpdateCursorStages
    {
        public const string NoUpdates = "NoUpdates";
        public const string Start = "Start";
        public const string EvictStallJobs = "EvictStallJobs";
    }

    private record JobEvent
    {
        public PropagationContext? PropagationContext { get; private set; }
        private JobEvent(PropagationContext? propagationContext) => PropagationContext = propagationContext;

        public record SignalContinuation(MLSearch_SignalChatIndexingContinuation Event, PropagationContext? PropagationContext) : JobEvent(PropagationContext);
        public record SignalCompletion(MLSearch_SignalChatIndexingCompletion Event, PropagationContext? PropagationContext) : JobEvent(PropagationContext);
    }

    // # Delegates
    public delegate ValueTask ScheduleJobHandler(
        ChatInfo chatInfo, SharedState state, RetrySettings retrySettings,
        IQueues queues,
        MomentClock clock,
        ILogger log,
        CancellationToken cancellationToken);
    public delegate void ContinueJobHandler(
        MLSearch_SignalChatIndexingContinuation evt, Moment moment, SharedState state);
    public delegate void CompleteJobHandler(
        MLSearch_SignalChatIndexingCompletion evt, SharedState state);
    public delegate ValueTask UpdateCursorHandler(
        Moment moment, SharedState state, TimeSpan stallJobTimeout,
        ICursorStates<Cursor> cursorStates,
        ILogger log,
        Tracer? tracer = null,
        CancellationToken cancellationToken = default);

    // # Fields
    private object _lock = new();
    private Channel<JobEvent>? _events;
    private Channel<JobEvent> Events => LazyInitializer.EnsureInitialized(
        ref _events,
        ref _lock,
        () => Channel.CreateBounded<JobEvent>(
            new BoundedChannelOptions(InputBufferCapacity) {
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

    public TimeSpan StallJobTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public ScheduleJobHandler OnScheduleJob { get; init; } = ScheduleIndexingJobAsync;
    public ContinueJobHandler OnContinueJob { get; init; } = HandleContinuationEvent;
    public CompleteJobHandler OnCompleteJob { get; init; } = HandleCompletionEvent;
    public UpdateCursorHandler OnUpdateCursor { get; init; } = UpdateCursorAsync;

    // # Public API methods
    public async ValueTask PostAsync(MLSearch_SignalChatIndexingContinuation evt, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(PostChatIndexingContinuationActivityName, ActivityKind.Consumer);
        var propagationContext = activity == null
            ? default(PropagationContext?)
            : new PropagationContext(activity.Context, Baggage.Current);

        await Events.Writer.WriteAsync(new JobEvent.SignalContinuation(evt, propagationContext), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PostAsync(MLSearch_SignalChatIndexingCompletion evt, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity(PostChatIndexingCompletionActivityName, ActivityKind.Consumer);
        var propagationContext = activity == null
            ? default(PropagationContext?)
            : new PropagationContext(activity.Context, Baggage.Current);

        await Events.Writer.WriteAsync(new JobEvent.SignalCompletion(evt, propagationContext), cancellationToken).ConfigureAwait(false);
    }

    public async Task UseAsync(CancellationToken cancellationToken = default)
    {
        var cursor = await cursorStates.LoadAsync(CursorKey, cancellationToken).ConfigureAwait(false) ?? new(0);
        var state = new SharedState(cursor, MaxConcurrency);

        var chats = chatSequence
            .LoadAsync(cursor.LastVersion, cancellationToken)
            .Select(info => new ChatInfo(info));
        var jobEvents = ReadJobEvents(cancellationToken);
        var updateCursorMoments = ReadUpdateCursorMoments(cancellationToken);

        await Task.WhenAll([
            RunAsync(chats, ScheduleJobForChatAsync, state, cancellationToken),
            RunAsync(jobEvents, HandleJobEventAsync, state, cancellationToken),
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
    private async ValueTask ScheduleJobForChatAsync(ChatInfo chatInfo, SharedState state, CancellationToken cancellationToken)
    {
        using var _ = ActivitySource.StartActivity(OnScheduleJobActivityName, ActivityKind.Internal);

        await OnScheduleJob(chatInfo, state, ScheduleJobRetrySettings, queues, clock, log, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask ScheduleIndexingJobAsync(
        ChatInfo chatInfo,
        SharedState state,
        RetrySettings retrySettings,
        IQueues queues,
        MomentClock clock,
        ILogger log,
        CancellationToken cancellationToken)
    {
        await state.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        var (chatId, version) = chatInfo;

        await AsyncChain.From(ct => ScheduleChatIndexing(chatId, queues, ct))
                .WithTransiencyResolver(retrySettings.TransiencyResolver)
                .Log(LogLevel.Debug, log)
                .Retry(retrySettings.RetryDelaySeq, retrySettings.AttemptCount, clock, log)
                .Run(cancellationToken)
            .ConfigureAwait(false);

        state.ScheduledJobs[chatId] = (version, clock.Now);

        return;

        static Task ScheduleChatIndexing(ChatId chatId, IQueues queues, CancellationToken cancellationToken)
        {
            var job = new MLSearch_TriggerChatIndexing(chatId, IndexingKind.ChatContent);
            return queues.Enqueue(job, cancellationToken);
        }
    }

    // ## Handle job completion events
    private async IAsyncEnumerable<JobEvent> ReadJobEvents([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var jobEvent = await Events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            yield return jobEvent;
        }
        // ReSharper disable once IteratorNeverReturns
    }

    private ValueTask HandleJobEventAsync(
        JobEvent data,
        SharedState state,
        CancellationToken _ct)
    {
        var otelContext = data.PropagationContext;
        Activity? activity = null;
        if (otelContext.HasValue) {
            var context = otelContext.Value;
            Baggage.Current = context.Baggage;
            activity = ActivitySource.StartActivity(OnHandleJobEventActivityName, ActivityKind.Consumer, context.ActivityContext);
        }
        using var __ = activity;

        switch (data) {
            case JobEvent.SignalContinuation { Event: var evt }:
                _ = activity?.SetTag("EventType", nameof(MLSearch_SignalChatIndexingContinuation));
                OnContinueJob(evt, clock.Now, state);
                break;
            case JobEvent.SignalCompletion { Event: var evt }:
                _ = activity?.SetTag("EventType", nameof(MLSearch_SignalChatIndexingCompletion));
                OnCompleteJob(evt, state);
                break;
            default:
                break;
        }
        return ValueTask.CompletedTask;
    }

    public static void HandleContinuationEvent(
        MLSearch_SignalChatIndexingContinuation evt, Moment moment, SharedState state)
    {
        _ = Interlocked.Increment(ref state.EventCount);
        if (state.ScheduledJobs.TryGetValue(evt.Id, out var info) && info is var (version, _)) {
            _ = state.ScheduledJobs.TryUpdate(evt.Id, (version, moment), info);
        }
    }

    public static void HandleCompletionEvent(
        MLSearch_SignalChatIndexingCompletion evt, SharedState state)
    {
        _ = Interlocked.Increment(ref state.EventCount);
        if (state.ScheduledJobs.TryRemove(evt.Id, out var info) && info is var (version, _)) {
            if (Volatile.Read(ref state.MaxVersion) < version) {
                Volatile.Write(ref state.MaxVersion, version);
            }
            _ = state.Semaphore.Release();
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

    private async ValueTask UpdateCursorAsync(Moment updateMoment, SharedState state, CancellationToken cancellationToken)
    {
        using var _ = ActivitySource.StartActivity(OnUpdateCursorActivityName, ActivityKind.Internal);

        await OnUpdateCursor(updateMoment, state, StallJobTimeout, cursorStates, log, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask UpdateCursorAsync(
        Moment updateMoment,
        SharedState state,
        TimeSpan stallJobTimeout,
        ICursorStates<Cursor> cursorStates,
        ILogger log,
        Tracer? tracer = null,
        CancellationToken cancellationToken = default)
    {
        try {
            var eventCount = Volatile.Read(ref state.EventCount);
            if (eventCount == state.PrevEventCount) {
                // ReSharper disable once ExplicitCallerInfoArgument
                tracer?.Point(UpdateCursorStages.NoUpdates);
                // There is no point in advancing cursor as no job reported its completion.
                return;
            }
            // ReSharper disable once ExplicitCallerInfoArgument
            tracer?.Point(UpdateCursorStages.Start);

            state.PrevEventCount = eventCount;
            var pastMoment = updateMoment - stallJobTimeout;
            var stallJobs = new List<ChatId>();
            // This is max chat version where indexing is completed.
            // In the case our schedule is empty we may want to advance cursor till there.
            var nextVersion = Volatile.Read(ref state.MaxVersion) + 1;
            foreach (var (jobId, (version, timestamp)) in state.ScheduledJobs) {
                if (timestamp <= pastMoment) {
                    // Job is stall, so skipping its version.
                    stallJobs.Add(jobId);
                }
                else {
                    // Job is still in the scheduled state,
                    // so our cursor must not advance farther than that.
                    nextVersion = Math.Min(nextVersion, version);
                }
            }
            // ReSharper disable once ExplicitCallerInfoArgument
            tracer?.Point(UpdateCursorStages.EvictStallJobs);
            foreach (var jobId in stallJobs) {
                if (state.ScheduledJobs.TryRemove(jobId, out var info) && info is var (_, timestamp)) {
                    log.LogWarning("Evicting indexing job for chat #{JobId} which is stall for {Interval}.",
                        jobId, updateMoment - timestamp);
                    _ = state.Semaphore.Release();
                }
            }
            await cursorStates.SaveAsync(CursorKey, new Cursor(nextVersion), cancellationToken).ConfigureAwait(false);
            log.LogInformation("Indexing cursor is advanced to the chat version #{Version}", nextVersion);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            log.LogError(e, "Failed to update cursor state.");
            throw;
        }
    }
}
