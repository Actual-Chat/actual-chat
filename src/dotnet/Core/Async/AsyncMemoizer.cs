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
///   - Bounded mode advances the head pointer and severs evicted links. Lagging
///     consumers skip to the current bounded window rather than retaining the
///     whole evicted chain.
///   - <see cref="IAsyncDisposable"/> is the primary disposal contract — sync
///     <see cref="IDisposable.Dispose"/> only signals cancellation and returns
///     immediately, deferring cleanup to the Read task's natural exit.
/// </summary>
public class AsyncMemoizer<T> : WorkerBase, IAsyncMemoizer<T>
{
    private IAsyncEnumerator<T>? _source;
    private readonly int _capacity;

    // Shared head: sentinel whose .Next is the oldest readable item.
    // In bounded mode, _head advances forward when capacity is exceeded.
    // Subclasses access these via the CurrentHead/CurrentTail/TryAdvanceHead
    // members; direct field access stays private to keep the invariant
    // "single-writer producer" enforceable.
    private volatile Node _head;
    private volatile Node _tail;

    public int Capacity => _capacity;
    public bool IsUnbounded => _capacity == int.MaxValue;
    public int BufferedCount => _tail.Index - _head.Index;
    public int ProducedCount => _tail.Index;
    public Exception? Completion => _tail.Completion;
    public bool IsCompleted => Completion != null;

    public AsyncMemoizer(IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        : this(source, int.MaxValue, cancellationToken)
    { }

    public AsyncMemoizer(IAsyncEnumerable<T> source, int capacity, CancellationToken cancellationToken = default)
        : this(source, capacity, true, cancellationToken)
    { }

    protected AsyncMemoizer(
        IAsyncEnumerable<T> source,
        int capacity,
        bool mustStart,
        CancellationToken cancellationToken = default)
        : base(cancellationToken.CreateLinkedTokenSource())
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _source = source.GetAsyncEnumerator(StopToken);
        _tail = _head = new Node(default!, 0); // Sentinel
        if (mustStart)
            this.Start();
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        // Detach the chain from _head/_tail so unread buffered items can be GC'd, but preserve
        // the current head index rather than using _tail.Index. This lets lagging consumers drain
        // items still reachable via their local `current.Next` pointer, while bounded consumers
        // already behind the evicted head still fast-forward to completion.
        var sentinel = new Node(default!, _head.Index, AsyncMemoizer.SuccessfulCompletion);
        sentinel.SetResult();
        _tail = _head = sentinel;
        _source = null;
    }

    public IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default)
        => Replay(int.MaxValue, cancellationToken);

    public virtual IAsyncEnumerable<T> Replay(
        int tailSize,
        CancellationToken cancellationToken = default)
        => ReplayFrom(GetReplayStart(tailSize), cancellationToken);

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

    public (TState Value, int ProducedCount) FoldBuffered<TState>(TState seed, Func<TState, T, TState> folder)
    {
        while (true) {
            var head = _head;
            if (TryFoldFrom(head, head, seed, folder) is { } result)
                return (result.Value, result.Node.Index);
        }
    }

    public Task WhenChanged(int knownProducedCount)
    {
        // The producer trips the old tail's Task right after linking a new node, and Complete()
        // trips the current one - so the tail's Task is "something happened past knownProducedCount"
        var tail = _tail;
        return tail.Index == knownProducedCount ? tail.Task : Task.CompletedTask;
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

        EvictIfNeeded(newNode);

        // Publish: set Next first (so any waiter that wakes up sees a non-null Next),
        // advance the producer's tail, then trip the TCS to wake waiters.
        oldTail.Next = newNode;
        _tail = newNode;
        oldTail.TrySetResult();
    }

    /// <summary>
    /// Bounded-capacity eviction hook. Default implementation drops the oldest
    /// item(s) by advancing <see cref="CurrentHead"/> until the chain length
    /// is within <see cref="Capacity"/>. Subclasses may override to implement
    /// alternative policies (e.g. duration-based or keyframe-span eviction).
    /// </summary>
    /// <remarks>
    /// Called from the single-writer producer task before a new node is
    /// linked into the chain. Implementations may freely use
    /// <see cref="TryAdvanceHead"/> and inspect <see cref="CurrentHead"/> /
    /// <see cref="CurrentTail"/>; no concurrency precautions are needed.
    /// </remarks>
    protected virtual void EvictIfNeeded(Node newNode)
    {
        if (_capacity == int.MaxValue)
            return;
        while (newNode.Index - _head.Index > _capacity) {
            if (!TryAdvanceHead())
                break;
        }
    }

    protected async IAsyncEnumerable<T> ReplayFrom(
        Node from,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // `from` is the previous node - what gets yielded is its Next onward
        var current = from;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var head = _head;
            if (current.Index < head.Index) {
                current = head;
                continue;
            }

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

    protected Node GetReplayStart(int tailSize)
    {
        // We want to yield up to `tailSize` items already in the buffer plus all future items.
        // With a linked list and no random access, "skip oldest" is an O(N) walk — acceptable
        // because the common case is tailSize == int.MaxValue (no skip), and bounded buffers are
        // small in practice. The result is the "previous" node; the caller yields its Next onward.
        var current = _head;
        if (tailSize >= int.MaxValue)
            return current;

        var tail = _tail;
        var fromIndex = Math.Max(current.Index, tail.Index - Math.Max(0, tailSize));
        while (current.Index < fromIndex) {
            var next = current.Next;
            if (next == null)
                break; // reached tail; nothing to skip past

            current = next;
        }
        return current;
    }

    // Null when a concurrent eviction moved the head mid-walk, which would silently truncate the fold
    protected (Node Node, TState Value)? TryFoldFrom<TState>(
        Node head,
        Node from,
        TState state,
        Func<TState, T, TState> folder)
    {
        var current = from;
        while (current.Next is { } next) {
            current = next;
            state = folder(state, current.Value);
        }
        return ReferenceEquals(_head, head) ? (current, state) : null;
    }

    /// <summary>Current chain head (sentinel; oldest readable item is <c>CurrentHead.Next</c>).</summary>
    protected Node CurrentHead => _head;

    /// <summary>Current chain tail (most recently appended item).</summary>
    protected Node CurrentTail => _tail;

    /// <summary>
    /// Advance <see cref="CurrentHead"/> by one node and sever the evicted
    /// link. Returns false if the chain has no further nodes (head is at tail).
    /// Single-writer: only call from the producer task (i.e. inside an
    /// <see cref="EvictIfNeeded"/> override).
    /// </summary>
    protected bool TryAdvanceHead()
    {
        var oldHead = _head;
        var nextHead = oldHead.Next;
        if (nextHead == null)
            return false;
        _head = nextHead;
        oldHead.Next = null;
        return true;
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
    protected internal sealed class Node(T value, int index, Exception? completion = null)
        : TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        public readonly T Value = value;
        public readonly int Index = index;
        public volatile Node? Next; // written by producer (release), read by consumers (acquire)
        public Exception? Completion = completion; // non-null on the final node; siblings remain null
    }
}
