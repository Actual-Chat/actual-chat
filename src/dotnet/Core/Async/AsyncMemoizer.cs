using System.Runtime.ExceptionServices;

namespace ActualChat;

/// <summary>
/// Holds the sentinel exception used to represent successful completion of an
/// <see cref="IAsyncMemoizer{T}"/> stream. Both <see cref="AsyncMemoizer{T}"/>
/// and the legacy <c>OldAsyncMemoizer&lt;T&gt;</c> use the same instance so that
/// <c>completion is ChannelClosedException</c> checks remain consistent.
/// </summary>
public static class AsyncMemoizer
{
    public static readonly ChannelClosedException SuccessfulCompletion = new();
}

/// <summary>
/// Memoizes an async sequence into a singly-linked list of nodes that double as
/// <see cref="TaskCompletionSource"/> instances — one node (= one allocation) per
/// produced item. Consumers walk the chain lock-free, awaiting the current tail's
/// <see cref="Task"/> when no more items are available.
///
/// Design notes:
///   - No fan-out task: each consumer drives its own iteration directly over the
///     chain. There is no shared Write loop that could introduce duplication or
///     orphan-target races.
///   - The chain itself is the snapshot — no seqlock, no bounded-channel-of-targets.
///   - Bounded mode advances the head pointer; lagging consumers keep evicted
///     nodes alive via their local pointer, so memory is soft-bounded rather than
///     hard-bounded. (All production callers use unbounded.)
///   - <see cref="IAsyncDisposable"/> is the primary disposal contract — sync
///     <see cref="IDisposable.Dispose"/> only signals cancellation and returns
///     immediately, deferring cleanup to the Read task's natural exit.
/// </summary>
public sealed class AsyncMemoizer<T> : WorkerBase, IAsyncMemoizer<T>
{
    private IAsyncEnumerator<T>? _source;
    private readonly int _capacity;
    private readonly Action<T>? _onRemove; // invoked when an item is evicted from the head or on Dispose

    // Shared head: sentinel whose .Next is the oldest readable item.
    // In bounded mode, _head advances forward when capacity is exceeded.
    private volatile Node _head;
    private volatile Node _tail;

    public int Capacity => _capacity;
    public bool IsUnbounded => _capacity == int.MaxValue;
    public int BufferedCount => _tail.Index - _head.Index;
    public Exception? Completion => _tail.Completion;
    public bool IsCompleted => Completion != null;

    public AsyncMemoizer(IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        : this(source, int.MaxValue, null, cancellationToken)
    { }

    public AsyncMemoizer(IAsyncEnumerable<T> source, int capacity, CancellationToken cancellationToken = default)
        : this(source, capacity, null, cancellationToken)
    { }

    public AsyncMemoizer(
        IAsyncEnumerable<T> source,
        int capacity,
        Action<T>? onRemove,
        CancellationToken cancellationToken = default)
        : base(cancellationToken.CreateLinkedTokenSource())
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _onRemove = onRemove;
        _source = source.GetAsyncEnumerator(StopToken);
        _tail = _head = new Node(default!, 0); // Sentinel
        this.Start();
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        if (_onRemove != null) {
            for (var node = _head.Next; node != null; node = node.Next)
                _onRemove.Invoke(node.Value);
        }
        var sentinel = new Node(default!, _tail.Index, AsyncMemoizer.SuccessfulCompletion);
        sentinel.SetResult();
        _tail = _head = sentinel;
        _source = null;
    }

    public IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default)
        => Replay(int.MaxValue, cancellationToken);

    public async IAsyncEnumerable<T> Replay(
        int tailSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Pick the starting position. We want to yield up to `tailSize` items already in the
        // buffer plus all future items. With a linked list and no random access, "skip oldest"
        // is an O(N) walk — acceptable because the common case is tailSize == int.MaxValue
        // (no skip), and bounded buffers are small in practice.
        // current is the "previous" node; we yield current.Next. We walk forward until
        // current.Index >= fromIndex, where fromIndex is the index of the last item we want
        // to skip.
        var current = _head;
        if (tailSize < int.MaxValue) {
            var tail = _tail;
            var fromIndex = Math.Max(current.Index, tail.Index - Math.Max(0, tailSize));
            while (current.Index < fromIndex) {
                var next = current.Next;
                if (next == null)
                    break; // reached tail; nothing to skip past

                current = next;
            }
        }

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var next = current.Next;
            if (next != null) {
                current = next;
                yield return current.Value;
                continue;
            }
            // No next yet — either the stream is alive and we should wait, or it's completed.
            var completion = current.Completion;
            if (completion != null) {
                if (completion is ChannelClosedException)
                    yield break;
                ExceptionDispatchInfo.Capture(completion).Throw();
            }
            // Wait for producer to set Next (and call TrySetResult on this node).
            await current.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Convenience method: pipes the replay into a ChannelWriter rather than an IAsyncEnumerable.
    public Task AddReplayTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
        => AddReplayTarget(channel, int.MaxValue, cancellationToken);

    public async Task AddReplayTarget(
        ChannelWriter<T> channel,
        int tailSize,
        CancellationToken cancellationToken = default)
    {
        try {
            await foreach (var item in Replay(tailSize, cancellationToken).ConfigureAwait(false)) {
                if (!channel.TryWrite(item))
                    await channel.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            channel.TryComplete();
        }
        catch (ChannelClosedException) {
            // Target gone; stop quietly.
        }
        catch (Exception e) {
            channel.TryComplete(e);
            throw;
        }
    }

    // Protected and private methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var source = _source!; // captured locally; DisposeAsync may null the field after we exit
        try {
            while (await source.MoveNextAsync().ConfigureAwait(false)) {
                cancellationToken.ThrowIfCancellationRequested();
                AppendItem(source.Current);
            }
            Complete(AsyncMemoizer.SuccessfulCompletion);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Complete(e);
            ExceptionDispatchInfo.Capture(e).Throw();
        }
        finally {
            if (_tail.Completion == null)
                Complete(AsyncMemoizer.SuccessfulCompletion);
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Single-writer: only the Read task calls AppendItem and Complete.
    private void AppendItem(T item)
    {
        var oldTail = _tail;
        var newNode = new Node(item, oldTail.Index + 1);

        // Bounded eviction: drop oldest by advancing _head. Lagging consumers may still
        // hold references to older nodes via their local pointer; in that case the chain
        // stays live until they release it. This trades hard memory bounds for lock-free reads.
        if (_capacity != int.MaxValue) {
            while (newNode.Index - _head.Index > _capacity) {
                var nextHead = _head.Next;
                if (nextHead == null)
                    break;

                _onRemove?.Invoke(nextHead.Value);
                _head = nextHead;
            }
        }

        // Publish: set Next first (so any waiter that wakes up sees a non-null Next),
        // advance the producer's tail, then trip the TCS to wake waiters.
        oldTail.Next = newNode;
        _tail = newNode;
        oldTail.TrySetResult();
    }

    private void Complete(Exception completion)
    {
        // Set Completion on the current tail FIRST, then trip its TCS. A waiter that
        // wakes up will read Completion (non-null) since we wrote it before TrySetResult.
        var tail = _tail;
        tail.Completion = completion;
        tail.TrySetResult();
    }

    // Nested types

    // A Node IS a TaskCompletionSource — one allocation per item, not two.
    // - Index = 0 for the initial sentinel; Index = N for the N-th item (1-based).
    // - Next is set by the producer (single Read task), then TrySetResult is called.
    // - Completion is set by the producer on the final node alongside TrySetResult.
    private sealed class Node(T value, int index, Exception? completion = null)
        : TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        public readonly T Value = value;
        public readonly int Index = index;
        public volatile Node? Next; // written by producer (release), read by consumers (acquire)
        public Exception? Completion = completion; // non-null on the final node; siblings remain null
    }
}
