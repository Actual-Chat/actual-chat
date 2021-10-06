using System.Buffers;
using System.Reactive;

namespace ActualChat.Channels;

public sealed class AsyncMemoizer<T>
{
    private readonly IAsyncEnumerator<T> _source;
    private readonly List<ChannelWriter<T>> _targets = new();
    private readonly Channel<(ChannelWriter<T> Target, int CopiedItemCount)> _newTargets;
    private readonly ArrayBufferWriter<Result<T>> _bufferWriter;
    private volatile Buffer _buffer;

    public Task FillBufferTask { get; }
    public Task DistributeTask { get; }

    public AsyncMemoizer(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        _source = source.GetAsyncEnumerator(cancellationToken);
        _newTargets = Channel.CreateBounded<(ChannelWriter<T>, int)>(
            new BoundedChannelOptions(16) {
                SingleReader = true,
            });
        _bufferWriter = new ArrayBufferWriter<Result<T>>(16);
        _buffer = new Buffer(_bufferWriter.WrittenMemory);
        DistributeTask = Task.Run(() => Distribute(cancellationToken), cancellationToken);
        FillBufferTask = Task.Run(() => FillBuffer(cancellationToken),cancellationToken);
    }

    public async IAsyncEnumerable<T> Replay(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true,
            });
        await AddReplayTarget(channel, cancellationToken).ConfigureAwait(false);
        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        while (reader.TryRead(out var item))
            yield return item;
    }

    public async IAsyncEnumerable<T> Replay(
        int bufferSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = bufferSize != int.MaxValue
            ? Channel.CreateBounded<T>(new BoundedChannelOptions(bufferSize) {
                SingleReader = true,
                SingleWriter = true
            })
            : Channel.CreateUnbounded<T>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true,
            });
        await AddReplayTarget(channel, cancellationToken).ConfigureAwait(false);
        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        while (reader.TryRead(out var item))
            yield return item;
    }

    public async Task AddReplayTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
    {
        var skipCount = await _buffer.WriteTo(channel, 0, cancellationToken).ConfigureAwait(false);
        while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        while (_newTargets.Writer.TryWrite((channel, skipCount)))
            return;
        if (!DistributeTask.IsCompleted)
            await DistributeTask.SuppressCancellation().ConfigureAwait(false);
        await _buffer.WriteTo(channel, skipCount, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task FillBuffer(CancellationToken cancellationToken)
    {
        try {
            var readTask = _source.ReadResultAsync(cancellationToken);
            while (true) {
                var result = await readTask.ConfigureAwait(false);
                var memory = _bufferWriter.GetMemory(8);
                var index = 0;
                memory.Span[index++] = result;
                _bufferWriter.Advance(1);
                readTask = _source.ReadResultAsync(cancellationToken);
                while (!result.HasError && _bufferWriter.FreeCapacity > 0) {
                    if (!readTask.IsCompleted)
                        break;
                    result = await readTask; // Sync wait
                    memory.Span[index++] = result;
                    _bufferWriter.Advance(1);
                    readTask = _source.ReadResultAsync(cancellationToken);
                }
                var newBuffer = new Buffer(_bufferWriter.WrittenMemory);
                var oldBuffer = Interlocked.Exchange(ref _buffer, newBuffer);
                oldBuffer.MarkOutdated();
                if (result.HasError)
                    break;
            }
        }
        finally {
            _newTargets.Writer.TryComplete();
            await _source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task Distribute(CancellationToken cancellationToken)
    {
        var buffer = await SwitchToNewBuffer(null).ConfigureAwait(false);
        var newTargetReadTask = _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
        var hasMoreNewTargets = true;
        while (hasMoreNewTargets || !buffer.IsCompleted) {
            if (hasMoreNewTargets) {
                newTargetReadTask ??= _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
                await Task.WhenAny(newTargetReadTask, buffer.WhenOutdatedTask).ConfigureAwait(false);
            }
            else
                await buffer.WhenOutdatedTask.ConfigureAwait(false);

            if (buffer.WhenOutdatedTask.IsCompleted)
                buffer = await SwitchToNewBuffer(buffer).ConfigureAwait(false);

            if (hasMoreNewTargets && newTargetReadTask!.IsCompleted) {
                if ((await newTargetReadTask).IsSome(out var newTarget)) {
                    await buffer.WriteTo(newTarget.Target, newTarget.CopiedItemCount, cancellationToken).ConfigureAwait(false);
                    _targets.Add(newTarget.Target);
                    newTargetReadTask = null;
                }
                else
                    hasMoreNewTargets = false;
            }
        }

        async Task<Buffer> SwitchToNewBuffer(Buffer? oldBuffer)
        {
            var skipCount = oldBuffer?.Items.Length ?? 0;
            var newBuffer = _buffer;
            if (newBuffer == oldBuffer)
                return newBuffer;
            for (var i = skipCount; i < newBuffer.Items.Length; i++) {
                var item = newBuffer.Items.Span[i];
                foreach (var target in _targets)
                    await target.WriteResultAsync(item, cancellationToken);
            }
            return newBuffer;
        }
    }

    private class Buffer
    {
        public ReadOnlyMemory<Result<T>> Items { get; private set; }
        public Task<Unit> WhenOutdatedTask { get; }
        public bool IsCompleted => Items.Length > 0 && Items.Span[^1].HasError;

        public Buffer(ReadOnlyMemory<Result<T>> items)
        {
            Items = items;
            WhenOutdatedTask = TaskSource.New<Unit>(true).Task;
        }

        public void MarkOutdated()
            => TaskSource.For(WhenOutdatedTask).TrySetResult(default);

        public async ValueTask<int> WriteTo(
            ChannelWriter<T> channel,
            int skipCount,
            CancellationToken cancellationToken)
        {
            var items = Items;
            for (var i = skipCount; i < items.Length; i++)
                await channel.WriteResultAsync(items.Span[i], cancellationToken).ConfigureAwait(false);
            return items.Length;
        }
    }
}
