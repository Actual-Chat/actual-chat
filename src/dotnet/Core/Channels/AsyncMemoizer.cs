using System.Buffers;
using System.Runtime.ExceptionServices;

namespace ActualChat.Channels;

public sealed class AsyncMemoizer<T>
{
    private readonly IAsyncEnumerator<T> _source;
    private readonly HashSet<ChannelWriter<T>> _targets = new();
    private readonly Channel<(ChannelWriter<T> Target, int CopiedItemCount)> _newTargets;
    private readonly ArrayBufferWriter<Result<T>> _bufferWriter;
    private volatile Buffer _buffer;

    public Task ReadTask { get; }
    public Task WriteTask { get; }

    public AsyncMemoizer(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        _source = source.GetAsyncEnumerator(cancellationToken);
        _newTargets = Channel.CreateBounded<(ChannelWriter<T>, int)>(
            new BoundedChannelOptions(Constants.Queues.AsyncMemoizerTargetQueueSize) {
                SingleReader = true,
            });
        _bufferWriter = new ArrayBufferWriter<Result<T>>(16);
        _buffer = new Buffer(_bufferWriter.WrittenMemory);
        WriteTask = BackgroundTask.Run(() => Write(cancellationToken), cancellationToken);
        ReadTask = BackgroundTask.Run(() => Read(cancellationToken), cancellationToken);
    }

    public async IAsyncEnumerable<T> Replay(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // AY: SingleWriter should be false!
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions() { SingleReader = true });
        await AddReplayTarget(channel, cancellationToken).ConfigureAwait(false);
        try {
            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (reader.TryRead(out var item))
                yield return item;
        }
        finally {
            channel.Writer.TryComplete();
        }
    }

    public async IAsyncEnumerable<T> Replay(
        int bufferSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // AY: SingleWriter should be false!
        var channel = bufferSize != int.MaxValue
            ? Channel.CreateBounded<T>(new BoundedChannelOptions(bufferSize) { SingleReader = true })
            : Channel.CreateUnbounded<T>(new UnboundedChannelOptions() { SingleReader = true });
        await AddReplayTarget(channel, cancellationToken).ConfigureAwait(false);
        try {
            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (reader.TryRead(out var item))
                yield return item;
        }
        finally {
            channel.Writer.TryComplete();
        }
    }

    public async Task AddReplayTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
    {
        var buffer = _buffer;
        var skipCount = buffer.Items.Length;
        var success = await buffer.TryCopyTo(channel, 0, cancellationToken).ConfigureAwait(false);
        if (!success)
            return;

        while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        while (_newTargets.Writer.TryWrite((channel, skipCount)))
            return;

        if (!WriteTask.IsCompleted)
            await WriteTask.SuppressCancellationAwait(false);
        await _buffer.TryCopyTo(channel, skipCount, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task Read(CancellationToken cancellationToken)
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

                    result = await readTask.ConfigureAwait(false); // Sync wait
                    memory.Span[index++] = result;
                    _bufferWriter.Advance(1);
                    readTask = _source.ReadResultAsync(cancellationToken);
                }
                var newBuffer = new Buffer(_bufferWriter.WrittenMemory);
                var oldBuffer = Interlocked.Exchange(ref _buffer, newBuffer);
                oldBuffer.MarkOutdated();
                var resultError = result.Error;
                if (resultError != null) {
                    if (resultError is ChannelClosedException)
                        break;
                    ExceptionDispatchInfo.Capture(resultError).Throw();
                }
            }
        }
        finally {
            _newTargets.Writer.TryComplete();
            await _source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task Write(CancellationToken cancellationToken)
    {
        var closedTargets = new HashSet<ChannelWriter<T>>();
        var buffer = await SwitchToNewBuffer(null).ConfigureAwait(false);
        var newTargetsReadTask = _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
        while (newTargetsReadTask != null || !buffer.IsCompleted) {
            if (newTargetsReadTask != null)
                await Task.WhenAny(newTargetsReadTask, buffer.WhenOutdated).ConfigureAwait(false);
            else
                await buffer.WhenOutdated.ConfigureAwait(false);

            if (buffer.WhenOutdated.IsCompleted)
                // No need to await for WhenOutdatedTask - it never fails or gets cancelled
                buffer = await SwitchToNewBuffer(buffer).ConfigureAwait(false);

            if (newTargetsReadTask is { IsCompleted: true }) {
#pragma warning disable MA0004
                var newTargetReads = await newTargetsReadTask;
#pragma warning restore MA0004
                if (newTargetReads.IsSome(out var newTarget)) {
                    var success = await buffer
                        .TryCopyTo(newTarget.Target, newTarget.CopiedItemCount, cancellationToken)
                        .ConfigureAwait(false);
                    if (success)
                        _targets.Add(newTarget.Target);
                    newTargetsReadTask = _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
                }
                else
                    newTargetsReadTask = null;
            }
        }

        async Task<Buffer> SwitchToNewBuffer(Buffer? oldBuffer)
        {
            var skipCount = oldBuffer?.Items.Length ?? 0;
            var newBuffer = _buffer;
            if (newBuffer == oldBuffer)
                return newBuffer;
            foreach (var target in _targets) {
                try {
                    for (var i = skipCount; i < newBuffer.Items.Length; i++) {
                        var item = newBuffer.Items.Span[i];
                        await target.WriteResultAsync(item, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (ChannelClosedException) {
                    closedTargets.Add(target);
                }
            }
            if (closedTargets.Count != 0) {
                foreach (var closedTarget in closedTargets)
                    _targets.Remove(closedTarget);
                closedTargets.Clear();
            }
            return newBuffer;
        }
    }

    private class Buffer
    {
        private readonly TaskCompletionSource _whenOutdatedSource = TaskCompletionSourceExt.New();

        public ReadOnlyMemory<Result<T>> Items { get; }
        public Task WhenOutdated => _whenOutdatedSource.Task;
        public bool IsCompleted => Items.Length > 0 && Items.Span[^1].HasError;

        public Buffer(ReadOnlyMemory<Result<T>> items)
            => Items = items;

        public void MarkOutdated()
            => _whenOutdatedSource.TrySetResult();

        public async ValueTask<bool> TryCopyTo(
            ChannelWriter<T> channel,
            int skipCount,
            CancellationToken cancellationToken)
        {
            try {
                var items = Items;
                for (var i = skipCount; i < items.Length; i++)
                    await channel.WriteResultAsync(items.Span[i], cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (ChannelClosedException) {
                return false;
            }
        }
    }
}
