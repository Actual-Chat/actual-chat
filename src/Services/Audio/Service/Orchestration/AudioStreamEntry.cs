using System.Collections.Generic;
using System.Reactive;
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
        }

        public Symbol StreamId { get; }
        public AudioRecording AudioRecording { get; }

        public ChannelReader<AudioMessage> GetStream()
        {
            if (Interlocked.CompareExchange(ref _buffering, 1, 0) == 0) _ = StartBuffering();

            var readChannel = Channel.CreateUnbounded<AudioMessage>(
                new UnboundedChannelOptions { SingleWriter = true });
            _readChannels.Writer.TryWrite(readChannel.Writer);
            return readChannel.Reader;
        }

        private async Task StartBuffering()
        {
            var cts = new CancellationTokenSource();
            var readers = _readChannels.Reader;
            var messages = _audioStream;
            var readerAvailable = true;
            while (true) {
                var watForReader = readerAvailable 
                    ? readers.WaitToReadAsync(cts.Token).AsTask()
                    : Task.FromResult(false);
                // ReSharper disable once MethodSupportsCancellation
                var waitForMessage = messages.WaitToReadAsync().AsTask();
                if (readerAvailable)
                    await Task.WhenAny(watForReader, waitForMessage);
                if (readerAvailable && watForReader.IsCompleted) {
                    readerAvailable = await watForReader;
                    if (readerAvailable)
                        while (readers.TryRead(out var reader)) {
                            foreach (var bufferedMessage in _buffer) await reader.WriteAsync(bufferedMessage, cts.Token);
                            _activeReadChannels.Add(reader);
                        }
                }
                if (waitForMessage.IsCompleted)
                {
                    var messageAvailable = await waitForMessage;
                    if (!messageAvailable) {
                    
                    }
                    else {
                    
                    }
                }
            }
            // await foreach (var audioMessage in _audioStream.ReadAllAsync()) {
            //     _buffer.Add(audioMessage);
            // }

            throw new System.NotImplementedException();
        }

        // TODO(AK): Actually we can build precise Cue index with bit-perfect offset to blocks\clusters
        public async Task<IReadOnlyList<AudioMetaDataEntry>> GetMetaDataOnCompletion(CancellationToken cancellationToken)
        {
            await _audioStream.Completion.WithFakeCancellation(cancellationToken);
            return _metaDataEntries;
        }
        
    }
}