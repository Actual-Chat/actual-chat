using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl;
using Stl.Async;

namespace ActualChat.Channels
{
    public sealed class ChannelCopier<T> : AsyncProcessBase
    {
        public ChannelReader<T> Source { get; }
        private readonly List<ChannelWriter<T>> _targets = new();
        private readonly Channel<(ChannelWriter<T> Target, int CopiedItemCount)> _newTargets;
        private volatile ImmutableList<Result<T>> _buffer = ImmutableList<Result<T>>.Empty;

        public ChannelCopier(ChannelReader<T> source)
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

                    if (newTargetTask.IsCompleted) {
                        if ((await newTargetTask).IsSome(out var newTargetDef)) {
                            var (newTarget, copiedItemCount) = newTargetDef;
                            await CopyBuffer(newTarget, copiedItemCount, cancellationToken).ConfigureAwait(false);
                            _targets.Add(newTarget);
                        }
                        else
                            // The copier is disposed
                            mustContinue = false;
                        newTargetTask = null;
                    }

                    if (newItemTask.IsCompleted) {
                        var newItem = await newItemTask;
                        _buffer = _buffer.Add(newItem);
                        foreach (var target in _targets)
                            await target.WriteResultAsync(newItem, cancellationToken);
                        if (newItem.Error is ChannelClosedException)
                            mustContinue = false;
                        newItemTask = null;
                    }
                } while (mustContinue);
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
