using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Services.Internal;

public sealed class NavigationQueue
{
    private readonly LinkedList<Entry> _queue = new();
    private readonly HashSet<long> _completedItemIds = new();
    private volatile Entry? _lastProcessedEntry;
    private Dispatcher? _dispatcher;

    internal ILogger Log { get; }
    internal ILogger? DebugLog { get; }
    internal Dispatcher Dispatcher => _dispatcher ??= History.Dispatcher;

    // ReSharper disable once InconsistentlySynchronizedField
    public History History { get; }
    public bool IsEmpty => _queue.First == null;
    public Entry? LastProcessedEntry => _lastProcessedEntry;

    public NavigationQueue(History history)
    {
        History = history;
        var services = History.Services;
        Log = services.LogFor(GetType());
        DebugLog = Log.IfEnabled(LogLevel.Debug);
    }

    public Task WhenLastEntryCompleted(CancellationToken cancellationToken = default)
    {
        Dispatcher.AssertAccess();
        var entry = LastProcessedEntry;
        return entry == null
            ? Task.CompletedTask
            : entry.WhenCompleted.WaitAsync(cancellationToken);
    }

    public async Task WhenAllEntriesCompleted(CancellationToken cancellationToken = default)
    {
        Dispatcher.AssertAccess();
        while (true) {
            var entry = LastProcessedEntry;
            if (entry != null)
                await entry.WhenCompleted.WaitAsync(cancellationToken).SilentAwait();
            cancellationToken.ThrowIfCancellationRequested();
            if (LastProcessedEntry == entry)
                break;
        }
    }

    // Internal methods

    internal Entry Enqueue(bool addInFront, string title, Func<long?> action)
    {
        Dispatcher.AssertAccess();
        var entry = new Entry(this, title, action);
        DebugLog?.LogDebug(
            "Enqueue({HeadOrTail}, \"{Comment}\"): queue size = {Count}",
            addInFront ? "head" : "tail", title, _queue.Count);
        if (addInFront)
            _queue.AddFirst(entry);
        else
            _queue.AddLast(entry);
        _ = ProcessNext();
        return entry;
    }

    internal async Task ProcessNext()
    {
        await WhenLastEntryCompleted().SilentAwait();
        if (_lastProcessedEntry is { WhenCompleted.IsCompleted: false })
            return; // Another call to ProcessNextInternal is doing the job already

        while (true) {
            var head = _queue.First;
            if (head == null)
                return;

            _queue.Remove(head);
            var entry = _lastProcessedEntry = head.Value;
            _completedItemIds.Clear();
            entry.Invoke();
            if (entry.ExpectedId is not { } vExpectedId)
                continue; // Invocation failed or no-op

            if (_completedItemIds.Count == 0)
                return; // entry.Invoke() will complete asynchronously or nothing to complete here

            // entry.Invoke() does everything synchronously in WASM
            if (vExpectedId == 0 || _completedItemIds.Contains(vExpectedId)) {
                if (entry.TryComplete(false))
                    continue; // We just completed the entry, so it's fine to continue the loop
            }

            // We couldn't complete the entry.
            // Once it completes asynchronously or times out, ProcessNext will be invoked anyway.
            break;
        }
    }

    internal bool TryComplete(long itemId)
    {
        if (_lastProcessedEntry?.ExpectedId is { } vExpectedId
            && (vExpectedId == 0 || vExpectedId == itemId)
            && _lastProcessedEntry.TryComplete(true))
            return true;

        _completedItemIds.Add(itemId); // This allows ProcessNext to complete the entry synchronously
        return false;
    }

    internal void Clear()
    {
        DebugLog?.LogDebug("Clear(): {Count} item(s) removed", _queue.Count);
        _queue.Clear();
    }

    // Nested types

    public class Entry
    {
        private static readonly string TypeName = $"{nameof(NavigationQueue)}.{nameof(Entry)}";

        private readonly TaskCompletionSource _whenCompletedSource = TaskCompletionSourceExt.New();
        private CancellationTokenSource? _timeoutSource;
        private CancellationToken _timeoutToken;

        public NavigationQueue Queue { get; }
        public string Title { get; }
        public Func<long?> Action { get; }
        public long? ExpectedId { get; private set; }
        public Task WhenCompleted => _whenCompletedSource.Task;

        public Entry(NavigationQueue queue, string title, Func<long?> action)
        {
            Queue = queue;
            Title = title;
            Action = action;
        }

        public override string ToString()
        {
            var sCompleted = (WhenCompleted.IsCompleted ? "completed" : "not completed yet");
            return $"{TypeName}(\"{Title}\", ExpectedId: #{ExpectedId} - {sCompleted})";
        }

        internal void Invoke()
        {
            try {
                Queue.DebugLog?.LogDebug("Invoking: {Entry}", this);
                ExpectedId = Action.Invoke();
                if (!ExpectedId.HasValue) {
                    // No navigation happened
                    TryComplete(true);
                    return;
                }
            }
            catch (Exception e) {
                Queue.Log.LogError(e, "Invoke failed for entry: {Entry}", this);
                TryComplete(true, e);
                return;
            }

            _timeoutSource = new CancellationTokenSource(History.MaxNavigationDuration);
            _timeoutToken = _timeoutSource.Token;
            _timeoutToken.Register(static state => {
                var self = (Entry)state!;
                _ = self.Queue.Dispatcher.InvokeAsync(() => {
                    var error = new TimeoutException("The navigation haven't completed on time.");
                    return self.TryComplete(true, error);
                });
            }, this);
        }

        internal bool TryComplete(bool mustProcessNext, Exception? error = null)
        {
            if (error != null) {
                if (!_whenCompletedSource.TrySetException(error))
                    return false;
            }
            else if (!_whenCompletedSource.TrySetResult())
                return false;

            _timeoutSource.DisposeSilently();
            if (_whenCompletedSource.Task.IsCompletedSuccessfully) {
                Queue.DebugLog?.LogDebug("Entry completed: {Entry}", this);
                if (mustProcessNext)
                    _ = Queue.ProcessNext();
            }
            else {
                var isTimedOut = _timeoutToken.IsCancellationRequested;
                if (isTimedOut) {
                    Queue.Log.LogError("Entry timed out: {Entry}", this);
                    // Running the rest after timeout may cause weird "delayed" side-effects
                    // (e.g. panels moving by themselves), so...
                    Queue.Clear();
                }
                else
                    Queue.Log.LogError("Entry failed: {Entry}", this);
            }
            return true;
        }
    }
}
