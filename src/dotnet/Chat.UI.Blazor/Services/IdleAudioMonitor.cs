using Stl.Internal;

namespace ActualChat.Chat.UI.Blazor.Services;

internal sealed class IdleAudioMonitor : IAsyncDisposable
{
    private readonly Dictionary<ChatId, (Task Task, CancellationTokenSource TokenSource)> _running = new ();
    private readonly object _lock = new ();
    private volatile bool _isDisposed;
    private Session Session { get; }
    private IChats Chats { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public IdleAudioMonitor(IServiceProvider services)
    {
        Clocks = services.Clocks();
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        Log = services.LogFor<IdleAudioMonitor>();
    }

    public void StartMonitoring(ChatId chatId, Func<ChatId, State, Task> callback, Options options, CancellationToken cancellationToken)
        => StartMonitoring(new[] { chatId }, callback, options, cancellationToken);

    public void StartMonitoring(IReadOnlyList<ChatId> chatIds,Func<ChatId, State, Task> callback, Options options, CancellationToken cancellationToken)
    {
        options.Validate();
        if (chatIds.Count == 0)
            return;

        ThrowIfDisposed();
        lock (_lock) {
            ThrowIfDisposed();
            foreach (var chatId in chatIds) {
                if (_running.ContainsKey(chatId))
                    throw StandardError.Constraint($"There is already running monitoring operation for chatId={chatId}");

                var cts = cancellationToken.CreateLinkedTokenSource();
                var monitoringTask = Monitor(chatId, callback, options, cts.Token);
                _running.Add(chatId, (monitoringTask, cts));
            }
        }
    }

    public Task StopMonitoring(ChatId chatId)
        => StopMonitoring(new[] { chatId });

    public async Task StopMonitoring(IReadOnlyList<ChatId> chatIds)
    {
        if (chatIds.Count == 0)
            return;

        var stoppedTasks = new List<Task>();
        ThrowIfDisposed();
        lock (_lock) {
            ThrowIfDisposed();
            foreach (var chatId in chatIds) {
                if (!_running.Remove(chatId, out var monitor))
                    throw StandardError.Constraint($"No running monitoring operation for chatId={chatId}");

                monitor.TokenSource.CancelAndDisposeSilently();
                if (!monitor.Task.IsCompleted)
                    stoppedTasks.Add(monitor.Task);
            }
        }

        try {
            await Task.WhenAll(stoppedTasks);
        }
        catch (OperationCanceledException) { }
        catch (Exception exc) {
            Log.LogError(exc, "Error occured in idle audio monitoring tasks");
            throw;
        }
    }

    private async Task Monitor(
        ChatId chatId,
        Func<ChatId, State, Task> callback,
        Options options,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var clock = Clocks.SystemClock;
        var monitoringStartedAt = clock.Now;
        await callback(chatId, State.NotIdle);
        // no need to check last entry since monitoring has just started
        await Task.Delay(options.IdleTimeoutBeforeCountdown, cancellationToken)
            .ConfigureAwait(false);

        ChatEntry? prevLastEntry = null;
        while (!cancellationToken.IsCancellationRequested) {
            var lastEntry = await GetLastTranscribedEntry(chatId,
                    prevLastEntry?.LocalId,
                    monitoringStartedAt,
                    cancellationToken)
                .ConfigureAwait(false);
            var lastEntryAt = lastEntry != null
                ? GetEndsAt(lastEntry)
                : monitoringStartedAt;
            lastEntryAt = Moment.Max(lastEntryAt, monitoringStartedAt);
            var willBeIdleAt = lastEntryAt + options.IdleTimeout;
            var timeBeforeStop = (willBeIdleAt - clock.Now).Positive();
            var timeBeforeCountdown =
                (lastEntryAt + options.IdleTimeoutBeforeCountdown - clock.Now).Positive();
            if (timeBeforeStop == TimeSpan.Zero) {
                // notify is idle and stop counting down
                await callback(chatId, State.Idle);
                return;
            }
            if (timeBeforeCountdown == TimeSpan.Zero) {
                // continue counting down
                await callback(chatId, State.Soon(willBeIdleAt));
                await Task.Delay(TimeSpanExt.Min(timeBeforeStop, options.CheckInterval),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else {
                // reset countdown since there were new messages
                await callback(chatId, State.NotIdle);
                await Task.Delay(timeBeforeCountdown, cancellationToken).ConfigureAwait(false);
            }
            prevLastEntry = lastEntry;
        }
    }

    private async Task<ChatEntry?> GetLastTranscribedEntry(
        ChatId chatId,
        long? startFrom,
        Moment minEndsAt,
        CancellationToken cancellationToken)
    {
        var idRange = await Chats
            .GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        if (startFrom != null)
            idRange = (startFrom.Value, idRange.End);
        var reader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        return await reader.GetLastWhile(idRange,
            x => x.HasAudioEntry || x.IsStreaming,
            x => GetEndsAt(x.ChatEntry) >= minEndsAt && x.SkippedCount < 100,
            cancellationToken);
    }

    private static Moment GetEndsAt(ChatEntry lastEntry)
        => lastEntry.EndsAt ?? lastEntry.ContentEndsAt ?? lastEntry.BeginsAt;

    public sealed record State(bool IsIdle, Moment? WillBeIdleAt)
    {
        public static readonly State NotIdle = new (false, null);
        public static readonly State Idle = new (true, null);
        public static State Soon(Moment willBeIdleAt) => new (false, willBeIdleAt);
    }

    public async ValueTask DisposeAsync()
    {
        List<(Task Task, CancellationTokenSource TokenSource)> toDispose;

        if (_isDisposed)
            return;
        lock (_lock) {
            if (_isDisposed)
                return;
            _isDisposed = true;

            toDispose = _running.Values.ToList();
            _running.Clear();
            foreach (var running in toDispose)
                running.TokenSource.CancelAndDisposeSilently();
        }

        await toDispose.Select(x => x.Task).Collect();
    }

    private void ThrowIfDisposed()
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (_isDisposed)
            throw Errors.AlreadyDisposed();
    }

    // Nested types

    public record Options(TimeSpan IdleTimeout,
        TimeSpan IdleTimeoutBeforeCountdown,
        TimeSpan CheckInterval)
    {
        public void Validate()
        {
            if (IdleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(IdleTimeout));
            if (CheckInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(CheckInterval));
            if (IdleTimeoutBeforeCountdown > IdleTimeout)
                throw new ArgumentOutOfRangeException(nameof(IdleTimeoutBeforeCountdown), IdleTimeoutBeforeCountdown, $"{nameof(IdleTimeoutBeforeCountdown)} cannot be greater than {nameof(IdleTimeout)}");
        }
    }
}
