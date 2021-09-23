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

        public async Task CompleteBuffering()
        {
            var originalState = Interlocked.Exchange(ref _buffering, 2);
            _newTargets.Writer.Complete();
            _completionSource.SetResult();

            if (originalState == 1) {
                Task? bufferingTask = null;
                while (bufferingTask == null) bufferingTask = Volatile.Read(ref _bufferingTask);
                await bufferingTask;
            }
            else {
                var startBufferingTask = StartBuffering(_cancellationTokenSource.Token);
                Volatile.Write(ref _bufferingTask, startBufferingTask);
                await startBufferingTask;
            }

            Volatile.Write(ref _buffering, 3);

            _cancellationTokenSource.Cancel();
        }

        private async Task StartBuffering(CancellationToken cancellationToken)
        {
            await Task.Yield();
            var readers = _newTargets.Reader;
            var messages = _source;
            var readerAvailable = true;
            var messageAvailable = true;
            while (readerAvailable || messageAvailable) {
                if (cancellationToken.IsCancellationRequested) {
                    foreach (var reader in _targets)
                        reader.Complete();
                    break;
                }
                var watForReader = readerAvailable
                    ? readers.WaitToReadAsync(cancellationToken).AsTask()
                    : Task.FromResult(false);
                var waitForMessage = messageAvailable
                    ? messages.WaitToReadAsync(cancellationToken).AsTask()
                    : Task.FromResult(false);
                if (readerAvailable)
                    await Task.WhenAny(watForReader, waitForMessage);
                else
                    await waitForMessage;

                if (readerAvailable && watForReader.IsCompleted) {
                    readerAvailable = await watForReader;
                    if (readerAvailable)
                        while (readers.TryRead(out var reader)) {
                            foreach (var bufferedMessage in _buffer)
                                await reader.WriteAsync(bufferedMessage, cancellationToken);
                            if (messageAvailable)
                                _targets.Add(reader);
                            else
                                reader.Complete();
                        }
                }

                if (messageAvailable && waitForMessage.IsCompleted) {
                    messageAvailable = await waitForMessage;
                    if (messageAvailable)
                        while (messages.TryRead(out var message)) {
                            _buffer.Add(message);
                            foreach (var reader in _targets)
                                await reader.WriteAsync(message, cancellationToken);
                        }
                    else {
                        foreach (var reader in _targets)
                            reader.Complete();
                        _targets.Clear();
                    }
                }
            }

            _targets.Clear();
        }
    }
}