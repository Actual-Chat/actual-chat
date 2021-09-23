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
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly TaskCompletionSource _completionSource;

        private int _buffering = 0;
        private Task? _bufferingTask;

        public Distributor2(ChannelReader<T> source)
        {
            _source = source;
            _buffer = new List<T>();
            _newTargets = Channel.CreateUnbounded<ChannelWriter<T>>(
                new UnboundedChannelOptions { SingleReader = true });
            _targets = new List<ChannelWriter<T>>();
            _cancellationTokenSource = new CancellationTokenSource();
            _completionSource = new TaskCompletionSource();
        }
        
        public Task? RunningTask => _bufferingTask;

        public async Task AddTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _buffering, 1, 0) == 0)
                _bufferingTask = StartBuffering(_cancellationTokenSource.Token);

            var buffering = Volatile.Read(ref _buffering);
            if (buffering != 1)
                _ = FillWithBuffered(channel, buffering);
            else
                _newTargets.Writer.TryWrite(channel);

            async Task FillWithBuffered(ChannelWriter<T> writer, int state)
            {
                if (state == 2) {
                    await Task.Yield();

                    Task? bufferingTask = null;
                    while (bufferingTask == null) bufferingTask = Volatile.Read(ref _bufferingTask);
                    await bufferingTask;
                }

                foreach (var item in _buffer)
                    await writer.WriteAsync(item);

                writer.Complete();
            }
        }

        public ChannelReader<T> GetStream()
        {
            if (Interlocked.CompareExchange(ref _buffering, 1, 0) == 0)
                _bufferingTask = StartBuffering(_cancellationTokenSource.Token);

            var readChannel = Channel.CreateUnbounded<T>(
                new UnboundedChannelOptions { SingleWriter = true });
            var buffering = Volatile.Read(ref _buffering);
            if (buffering != 1)
                _ = FillWithBuffered(readChannel.Writer, buffering);
            else
                _newTargets.Writer.TryWrite(readChannel.Writer);

            return readChannel.Reader;

            async Task FillWithBuffered(ChannelWriter<T> writer, int state)
            {
                if (state == 2) {
                    await Task.Yield();

                    Task? bufferingTask = null;
                    while (bufferingTask == null) bufferingTask = Volatile.Read(ref _bufferingTask);
                    await bufferingTask;
                }

                foreach (var item in _buffer)
                    await writer.WriteAsync(item);

                writer.Complete();
            }
        }

        private async Task StartBuffering(CancellationToken cancellationToken)
        {
            await Task.Yield();
            try {
                var targets = _newTargets.Reader;
                var items = _source;
                var itemAvailable = true;
                Task<bool>? newItemTask = null;
                Task<bool>? newTargetTask = null;
                while (itemAvailable) {
                    newItemTask ??= items.WaitToReadAsync(cancellationToken).AsTask();
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
                            while (items.TryRead(out var item)) {
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