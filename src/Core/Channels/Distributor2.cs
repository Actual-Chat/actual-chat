using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Channels
{
    public sealed class Distributor2<T>
    {
        private readonly ChannelReader<T> _source;
        private readonly List<T> _buffer;
        private readonly Channel<ChannelWriter<T>> _newTargets;
        private readonly List<ChannelWriter<T>> _targets;
        private readonly Task _bufferingTask;

        public Distributor2(ChannelReader<T> source)
        {
            _source = source;
            _buffer = new List<T>();
            _newTargets = Channel.CreateUnbounded<ChannelWriter<T>>(
                new UnboundedChannelOptions { SingleReader = true });
            _targets = new List<ChannelWriter<T>>();
            _bufferingTask = Task.Run(() => StartBuffering(CancellationToken.None));
        }
        
        public Task RunningTask => _bufferingTask;

        public async Task AddTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
        {
            if (_bufferingTask.IsCompleted)
                _ = FillWithBuffered(channel);
            else
                _newTargets.Writer.TryWrite(channel);

            async Task FillWithBuffered(ChannelWriter<T> target)
            {
                foreach (var item in _buffer)
                    await target.WriteAsync(item);

                target.Complete();
            }
        }

        private async Task StartBuffering(CancellationToken cancellationToken)
        {
            try {
                var targets = _newTargets.Reader;
                var itemAvailable = true;
                Task<bool>? newItemTask = null;
                Task<bool>? newTargetTask = null;
                while (itemAvailable) {
                    newItemTask ??= _source.WaitToReadAsync(cancellationToken).AsTask();
                    newTargetTask ??= targets.WaitToReadAsync(cancellationToken).AsTask();
                    await Task.WhenAny(newTargetTask, newItemTask).ConfigureAwait(false);

                    if (newTargetTask.IsCompleted) {
                        var targetAvailable = await newTargetTask;
                        newTargetTask = null;
                        if (targetAvailable)
                            while (targets.TryRead(out var target)) {
                                foreach (var item in _buffer)
                                    await target.WriteAsync(item, cancellationToken);
                                if (itemAvailable)
                                    _targets.Add(target);
                                else
                                    target.Complete();
                            }
                    }

                    if (itemAvailable && newItemTask.IsCompleted) {
                        itemAvailable = await newItemTask;
                        newItemTask = null;
                        if (itemAvailable)
                            while (_source.TryRead(out var item)) {
                                _buffer.Add(item);
                                foreach (var target in _targets)
                                    await target.WriteAsync(item, cancellationToken);
                            }
                    }
                }
            }
            catch (ChannelClosedException) {
                foreach (var target in _targets)
                    target.TryComplete();
                _targets.Clear();
            }
            catch (Exception ex) {
                foreach (var target in _targets)
                    target.Complete(ex);
                _targets.Clear();
            }
            
            _newTargets.Writer.Complete();
            while (await _newTargets.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (_newTargets.Reader.TryRead(out var newTarget)) {
                foreach (var item in _buffer)
                    await newTarget.WriteAsync(item, cancellationToken);
                _targets.Add(newTarget);
            }

            foreach (var target in _targets) 
                target.TryComplete();
        }
    }
}