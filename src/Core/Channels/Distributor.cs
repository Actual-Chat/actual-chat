using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl;
using Stl.Async;

namespace ActualChat.Channels
{
    public sealed class Distributor<T> : AsyncProcessBase
    {
        public ChannelReader<T> Source { get; }
        private readonly List<ChannelWriter<T>> _targets = new();
        private readonly Channel<(ChannelWriter<T> Target, int CopiedItemCount)> _newTargets;
        private volatile ImmutableList<Result<T>> _buffer = ImmutableList<Result<T>>.Empty;

        public Distributor(ChannelReader<T> source)
        {
            Source = source;
            _newTargets = Channel.CreateBounded<(ChannelWriter<T>, int)>(
                new BoundedChannelOptions(16) {
                    SingleReader = true,
                });
            _ = Run();
        }

        public async Task AddTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
        {
            var copiedItemCount = await CopyBuffer(channel, 0, cancellationToken).ConfigureAwait(false);
            while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false)) {
                if (_newTargets.Writer.TryWrite((channel, copiedItemCount)))
                    return;
            }
            await (RunningTask ?? Task.CompletedTask).ConfigureAwait(false);
            await CopyBuffer(channel, copiedItemCount, cancellationToken).ConfigureAwait(false);
        }

        // Protected methods

        protected override ValueTask DisposeInternal(bool disposing)
        {
            _newTargets.Writer.TryComplete();
            return base.DisposeInternal(disposing);
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            try {
                var mustContinue = true;
                Task<Result<T>>? newItemTask = null;
                Task<Option<(ChannelWriter<T> Target, int CopiedItemCount)>>? newTargetTask = null;
                do {
                    newItemTask ??= Source.ReadResultAsync(cancellationToken).AsTask();
                    newTargetTask ??= _newTargets.Reader.TryReadAsync(cancellationToken).AsTask();
                    await Task.WhenAny(newTargetTask, newItemTask).ConfigureAwait(false);

                    if (newItemTask.IsCompleted) {
                        var newItem = await newItemTask;
                        _buffer = _buffer.Add(newItem);
                        foreach (var target in _targets)
                            await target.WriteResultAsync(newItem, cancellationToken);
                        newItemTask = null;
                        if (newItem.Error is ChannelClosedException)
                            mustContinue = false;
                    }

                    if (newTargetTask.IsCompleted) {
                        if ((await newTargetTask).IsSome(out var newTarget)) {
                            await CopyBuffer(newTarget.Target, newTarget.CopiedItemCount, cancellationToken).ConfigureAwait(false);
                            _targets.Add(newTarget.Target);
                        }
                        else
                            // The distributor is disposed - we don't care about any items in this case
                            mustContinue = false;
                        newTargetTask = null;
                    }
                } while (mustContinue);

                _newTargets.Writer.TryComplete();
                if (Source.Completion.IsCompleted) {
                    // No more items, but we have to process the remaining new targets
                    while (await _newTargets.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    while (_newTargets.Reader.TryRead(out var newTarget)) {
                        await CopyBuffer(newTarget.Target, newTarget.CopiedItemCount, cancellationToken).ConfigureAwait(false);
                        _targets.Add(newTarget.Target);
                    }
                }
            }
            finally {
                _ = DisposeAsync();
            }
        }

        private async ValueTask<int> CopyBuffer(
            ChannelWriter<T> channel,
            int skipCount,
            CancellationToken cancellationToken)
        {
            var buffer = _buffer;
            for (var i = skipCount; i < buffer.Count; i++)
                await channel.WriteResultAsync(buffer[i], cancellationToken).ConfigureAwait(false);
            return buffer.Count;
        }
    }
}
