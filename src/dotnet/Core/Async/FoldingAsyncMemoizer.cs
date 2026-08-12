namespace ActualChat;

/// <summary>
/// An <see cref="AsyncMemoizer{T}"/> that keeps a running fold of everything produced so far, so a
/// reader pays only for what was appended since the last one. Given <c>toItem</c>, an unbounded
/// <see cref="Replay(int,CancellationToken)"/> also collapses the buffered prefix into a single
/// item, which is what a late subscriber wants: the state, not the history that built it.
/// </summary>
public sealed class FoldingAsyncMemoizer<T, TState> : AsyncMemoizer<T>
{
    private readonly TState _seed;
    private readonly Func<TState, T, TState> _folder;
    private readonly Func<TState, T>? _toItem;
    private Checkpoint? _checkpoint;

    public FoldingAsyncMemoizer(
        IAsyncEnumerable<T> source,
        TState seed,
        Func<TState, T, TState> folder,
        Func<TState, T>? toItem = null,
        int capacity = int.MaxValue,
        CancellationToken cancellationToken = default)
        : base(source, capacity, false, cancellationToken)
    {
        _seed = seed;
        _folder = folder;
        _toItem = toItem;
        this.Start();
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        Volatile.Write(ref _checkpoint, null);
    }

    public (TState Value, int ProducedCount) Fold()
    {
        var (node, value) = FoldPrefix();
        return (value, node.Index);
    }

    public override IAsyncEnumerable<T> Replay(int tailSize, CancellationToken cancellationToken = default)
    {
        // Collapsing the prefix contradicts "give me the last N items", so a bounded replay
        // stays verbatim. ReplayTailSize is int.MaxValue wherever the fold is the point.
        if (_toItem == null || tailSize < int.MaxValue)
            return base.Replay(tailSize, cancellationToken);

        return ReplayFolded(cancellationToken);
    }

    // Protected methods

    protected override void EvictIfNeeded(Node newNode)
    {
        base.EvictIfNeeded(newNode);
        // A checkpoint left behind the head pins the evicted chain through its Next links - the
        // very thing advancing the head is meant to release. Producer thread only, as documented.
        var checkpoint = Volatile.Read(ref _checkpoint);
        if (checkpoint != null && checkpoint.Node.Index < CurrentHead.Index)
            Interlocked.CompareExchange(ref _checkpoint, null, checkpoint);
    }

    // Private methods

    private async IAsyncEnumerable<T> ReplayFolded([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (node, value) = FoldPrefix();
        // Nothing buffered yet - a synthesized "empty" item would be noise the consumer has to skip
        if (node.Index > CurrentHead.Index)
            yield return _toItem!(value);

        await foreach (var item in ReplayFrom(node, cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    private (Node Node, TState Value) FoldPrefix()
    {
        while (true) {
            var head = CurrentHead;
            var checkpoint = Volatile.Read(ref _checkpoint);
            // A checkpoint at or ahead of the head is still linked in; an older one was evicted and
            // its Next severed, so the fold restarts - same window a bounded FoldBuffered covers.
            var isResumable = checkpoint != null && checkpoint.Node.Index >= head.Index;
            var from = isResumable ? checkpoint!.Node : head;
            var state = isResumable ? checkpoint!.Value : _seed;
            if (TryFoldFrom(head, from, state, _folder) is not { } result)
                continue;

            Publish(new Checkpoint(result.Node, result.Value));
            return result;
        }
    }

    private void Publish(Checkpoint next)
    {
        // Monotone: concurrent folders may duplicate work, but the checkpoint must never move back,
        // or a reader resuming from it would replay items that are already in its state.
        while (true) {
            var current = Volatile.Read(ref _checkpoint);
            if (current != null && current.Node.Index >= next.Node.Index)
                return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _checkpoint, next, current), current))
                return;
        }
    }

    // Nested types

    private sealed class Checkpoint(Node node, TState value)
    {
        public readonly Node Node = node;
        public readonly TState Value = value;
    }
}
