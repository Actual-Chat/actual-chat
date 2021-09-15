using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Streaming;
using Stl.Async;

namespace ActualChat.Audio.Orchestration
{
    public sealed class AudioRecordSegmentAccessor
    {
        private readonly WebMDocumentBuilder _documentBuilder;
        private readonly IReadOnlyList<AudioMetadataEntry> _metadata;
        private readonly double _offset;
        private readonly ChannelReader<BlobPart> _audioStream;
        private readonly List<BlobPart> _buffer;
        private readonly Channel<ChannelWriter<BlobPart>> _readChannels;
        private readonly List<ChannelWriter<BlobPart>> _activeReadChannels;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly TaskCompletionSource _completionSource;

        private int _buffering = 0;
        private Task? _bufferingTask;

        public AudioRecordSegmentAccessor(
            int index,
            AudioRecord audioRecord,
            WebMDocumentBuilder documentBuilder,
            IReadOnlyList<AudioMetadataEntry> metadata,
            double offset,
            ChannelReader<BlobPart> audioStream)
        {
            var streamId = new StreamId(audioRecord.Id, index);
            _documentBuilder = documentBuilder;
            _metadata = metadata;
            _offset = offset;
            AudioRecord = audioRecord;
            StreamId = streamId;
            Index = index;
            _audioStream = audioStream;
            _buffer = new List<BlobPart>();
            _readChannels = Channel.CreateUnbounded<ChannelWriter<BlobPart>>(
                new UnboundedChannelOptions { SingleReader = true });
            _activeReadChannels = new List<ChannelWriter<BlobPart>>();
            _cancellationTokenSource = new CancellationTokenSource();
            _completionSource = new TaskCompletionSource();
        }

        public StreamId StreamId { get; }
        public int Index { get; }
        public AudioRecord AudioRecord { get; }

        public ChannelReader<BlobPart> GetStream()
        {
            if (Interlocked.CompareExchange(ref _buffering, 1, 0) == 0)
                _bufferingTask = StartBuffering(_cancellationTokenSource.Token);

            var readChannel = Channel.CreateUnbounded<BlobPart>(
                new UnboundedChannelOptions { SingleWriter = true });
            var buffering = Volatile.Read(ref _buffering);
            if (buffering != 1)
                _ = FillWithBuffered(readChannel.Writer, buffering);
            else
                _readChannels.Writer.TryWrite(readChannel.Writer);

            return readChannel.Reader;

            async Task FillWithBuffered(ChannelWriter<BlobPart> writer, int state)
            {
                if (state == 2) {
                    await Task.Yield();

                    Task? bufferingTask = null;
                    while (bufferingTask == null) bufferingTask = Volatile.Read(ref _bufferingTask);
                    await bufferingTask;
                }

                foreach (var message in _buffer)
                    await writer.WriteAsync(message);

                writer.Complete();
            }
        }

        public async Task CompleteBuffering()
        {
            var originalState = Interlocked.Exchange(ref _buffering, 2);
            _readChannels.Writer.Complete();
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
            var readers = _readChannels.Reader;
            var messages = _audioStream;
            var readerAvailable = true;
            var messageAvailable = true;
            while (readerAvailable || messageAvailable) {
                if (cancellationToken.IsCancellationRequested) {
                    foreach (var reader in _activeReadChannels)
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
                                _activeReadChannels.Add(reader);
                            else
                                reader.Complete();
                        }
                }

                if (messageAvailable && waitForMessage.IsCompleted) {
                    messageAvailable = await waitForMessage;
                    if (messageAvailable)
                        while (messages.TryRead(out var message)) {
                            _buffer.Add(message);
                            foreach (var reader in _activeReadChannels)
                                await reader.WriteAsync(message, cancellationToken);
                        }
                    else {
                        foreach (var reader in _activeReadChannels)
                            reader.Complete();
                        _activeReadChannels.Clear();
                    }
                }
            }

            _activeReadChannels.Clear();
        }

        // TODO(AK): Actually we can build precise Cue index with bit-perfect offset to blocks\clusters
        public async Task<AudioStreamPart> GetPartOnCompletion(CancellationToken cancellationToken)
        {
            await _completionSource.Task.WithFakeCancellation(cancellationToken);
            return new AudioStreamPart(
                Index,
                StreamId,
                AudioRecord,
                _documentBuilder.ToDocument(),
                _metadata,
                _offset,
                _metadata.Sum(md => md.Duration));
        }

        public void Deconstruct(out StreamId streamId, out int index, out AudioRecord audioRecord)
        {
            streamId = StreamId;
            index = Index;
            audioRecord = AudioRecord;
        }
    }
}
