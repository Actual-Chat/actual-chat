using System.Buffers;
using System.Reactive;

namespace ActualChat.Channels;

public sealed class ChannelDistributor<T>
{
    private readonly ChannelReader<T> _source;
    private readonly List<ChannelWriter<T>> _targets = new();
    private readonly Channel<(ChannelWriter<T> Target, int CopiedItemCount)> _newTargets;
    private readonly ArrayBufferWriter<Result<T>> _bufferWriter;
    private volatile Buffer _buffer;

    public Task FillBufferTask { get; }
    public Task DistributeTask { get; }

    public ChannelDistributor(ChannelReader<T> source, CancellationToken cancellationToken)
    {
        _source = source;
        _newTargets = Channel.CreateBounded<(ChannelWriter<T>, int)>(
            new BoundedChannelOptions(16) {
                SingleReader = true,
            });
        _bufferWriter = new ArrayBufferWriter<Result<T>>(16);
        _buffer = new Buffer(_bufferWriter.WrittenMemory);
        DistributeTask = Distribute(cancellationToken);
        FillBufferTask = FillBuffer(cancellationToken);
    }

    public async Task AddTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
    {
        var buffer = _buffer;
        await buffer.WriteTo(channel, 0, cancellationToken).ConfigureAwait(false);
        var skipCount = buffer.Items.Length;
        while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false)) {
            if (_newTargets.Writer.TryWrite((channel, skipCount)))
                return;
        }
        if (!DistributeTask.IsCompleted)
            await DistributeTask.SuppressCancellation().ConfigureAwait(false);
        await _buffer.WriteTo(channel, skipCount, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task FillBuffer(CancellationToken cancellationToken)
    {
        try {
            while (true) {
                var result = await _source.ReadResultAsync(cancellationToken).ConfigureAwait(false);
                var memory = _bufferWriter.GetMemory(8);
                var index = 0;
                memory.Span[index++] = result;
                _bufferWriter.Advance(1);
                while (!result.HasError && _bufferWriter.FreeCapacity > 0 && _source.TryReadResult(out result)) {
                    memory.Span[index++] = result;
                    _bufferWriter.Advance(1);
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
        }
    }

    private async Task Distribute(CancellationToken cancellationToken)
    {
        var buffer = _buffer;
        var newTargetsClosedTask = _newTargets.Reader.Completion;
        var newTargetTask = _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
        while (!(buffer.IsCompleted && newTargetsClosedTask.IsCompleted)) {
            newTargetTask ??= _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
            await Task.WhenAny(newTargetTask, buffer.WhenOutdatedTask).ConfigureAwait(false);

            if (buffer.WhenOutdatedTask.IsCompleted) {
                var skipCount = buffer.Items.Length;
                buffer = _buffer;
                for (var i = skipCount; i < buffer.Items.Length; i++) {
                    var item = buffer.Items.Span[i];
                    foreach (var target in _targets)
                        await target.WriteResultAsync(item, cancellationToken);
                }
            }

            if (newTargetTask.IsCompleted) {
                if ((await newTargetTask).IsSome(out var newTarget)) {
                    await buffer.WriteTo(newTarget.Target, newTarget.CopiedItemCount, cancellationToken).ConfigureAwait(false);
                    _targets.Add(newTarget.Target);
                }
                newTargetTask = null;
            }
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

        public async ValueTask WriteTo(
            ChannelWriter<T> channel,
            int skipCount,
            CancellationToken cancellationToken)
        {
            var items = Items;
            for (var i = skipCount; i < items.Length; i++)
                await channel.WriteResultAsync(items.Span[i], cancellationToken).ConfigureAwait(false);
        }
    }
}
