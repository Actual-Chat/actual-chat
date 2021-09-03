using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Distribution;
using Stl.Async;
using Stl.Text;

namespace ActualChat.Audio.Orchestration
{
    public sealed class AudioStreamEntry
    {
        private readonly IReadOnlyList<AudioMetaDataEntry> _metaDataEntries;
        private readonly ChannelReader<AudioMessage> _audioStream;
        private readonly List<AudioMessage> _buffer;
        private readonly Channel<ChannelWriter<AudioMessage>> _readChannels;
        private readonly List<ChannelWriter<AudioMessage>> _activeReadChannels;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private int _buffering = 0;


        public AudioStreamEntry(
            AudioRecording audioRecording,
            Symbol streamId,
            IReadOnlyList<AudioMetaDataEntry> metaDataEntries,
            ChannelReader<AudioMessage> audioStream)
        {
            _metaDataEntries = metaDataEntries;
            AudioRecording = audioRecording;
            StreamId = streamId;
            _audioStream = audioStream;
            _buffer = new List<AudioMessage>();
            _readChannels = Channel.CreateUnbounded<ChannelWriter<AudioMessage>>(
                new UnboundedChannelOptions { SingleReader = true });
            _activeReadChannels = new List<ChannelWriter<AudioMessage>>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public Symbol StreamId { get; }
        public AudioRecording AudioRecording { get; }

        public ChannelReader<AudioMessage> GetStream()
        {
            if (Interlocked.CompareExchange(ref _buffering, 1, 0) == 0) 
                _ = StartBuffering(_cancellationTokenSource.Token);
            
            var readChannel = Channel.CreateUnbounded<AudioMessage>(
                new UnboundedChannelOptions { SingleWriter = true });
            if (Volatile.Read(ref _buffering) == 2)
                _ = FillWithBuffered(readChannel.Writer);
            else
                _readChannels.Writer.TryWrite(readChannel.Writer);

            return readChannel.Reader;

            async Task FillWithBuffered(ChannelWriter<AudioMessage>  writer)
            {
                foreach (var message in _buffer) 
                    await writer.WriteAsync(message);
                
                writer.Complete();
            }
        }

        public void CompleteBuffering()
        {
            if (Interlocked.CompareExchange(ref _buffering, 2, 1) != 1) return;
            
            _readChannels.Writer.Complete();
            _cancellationTokenSource.Cancel();
        }

        private async Task StartBuffering(CancellationToken cancellationToken)
        {
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
                    else
                        foreach (var reader in _activeReadChannels)
                            reader.Complete();
                }
            }
        }

        // TODO(AK): Actually we can build precise Cue index with bit-perfect offset to blocks\clusters
        public async Task<IReadOnlyList<AudioMetaDataEntry>> GetMetaDataOnCompletion(CancellationToken cancellationToken)
        {
            await _audioStream.Completion.WithFakeCancellation(cancellationToken);
            return _metaDataEntries;
        }
        
    }
}