using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Blobs;
using Stl.Async;

namespace ActualChat.Audio.Orchestration
{
    public sealed class AudioRecordSegment : AsyncProcessBase
    {
        private readonly WebMDocumentBuilder _webMBuilder;
        private readonly IReadOnlyList<AudioMetadataEntry> _metadata;
        private readonly double _offset;
        private readonly ChannelReader<BlobPart> _source;
        private readonly List<BlobPart> _readSourceParts;
        private readonly List<Channel<BlobPart>> _streams;
        private readonly Channel<Channel<BlobPart>> _newStreams;
        private AudioStreamPart? _audioStreamPart;

        public StreamId StreamId { get; }
        public int Index { get; }
        public AudioRecord AudioRecord { get; }
        public AudioStreamPart AudioStreamPart =>
            _audioStreamPart ??= new AudioStreamPart(
                Index,
                StreamId,
                AudioRecord,
                _webMBuilder.ToDocument(),
                _metadata,
                _offset,
                _metadata.Sum(md => md.Duration));


        public AudioRecordSegment(
            int index,
            AudioRecord audioRecord,
            WebMDocumentBuilder webMBuilder,
            IReadOnlyList<AudioMetadataEntry> metadata,
            double offset,
            ChannelReader<BlobPart> source)
        {
            Index = index;
            AudioRecord = audioRecord;
            StreamId = new StreamId(AudioRecord.Id, Index);
            _webMBuilder = webMBuilder;
            _metadata = metadata;
            _offset = offset;
            _source = source;
            _readSourceParts = new List<BlobPart>();
            _streams = new List<Channel<BlobPart>>();
            _newStreams = Channel.CreateUnbounded<Channel<BlobPart>>(
                new UnboundedChannelOptions {
                    SingleReader = true
                });
        }

        public ChannelReader<BlobPart> GetStream()
        {
            _ = Run();
            var channel = Channel.CreateUnbounded<BlobPart>(
                new UnboundedChannelOptions {
                    SingleWriter = true
                });
            _newStreams.Writer.TryWrite(channel);
            return channel.Reader;
        }

        // Protected methods

        protected override ValueTask DisposeInternal(bool disposing)
        {
            _newStreams.Writer.Complete();
            return base.DisposeInternal(disposing);
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            var neverEndingBoolTask = TaskSource.New<bool>(true).Task;
            try {
                var hasMoreNewStreams = true;
                var hasMoreNewParts = true;
                while (hasMoreNewStreams || hasMoreNewParts) {
                    var hasNewStreamTask = hasMoreNewStreams
                        ? _newStreams.Reader.WaitToReadAsync(cancellationToken).AsTask()
                        : neverEndingBoolTask;
                    var hasNewPartTask = hasMoreNewParts
                        ? _source.WaitToReadAsync(cancellationToken).AsTask()
                        : neverEndingBoolTask;
                    await Task.WhenAny(hasNewStreamTask, hasNewPartTask);

                    if (hasMoreNewStreams && hasNewStreamTask.IsCompleted) {
                        hasMoreNewStreams = await hasNewStreamTask;
                        while (hasMoreNewStreams && _newStreams.Reader.TryRead(out var newStream)) {
                            foreach (var part in _readSourceParts)
                                await newStream.Writer.WriteAsync(part, cancellationToken);
                            _streams.Add(newStream);
                        }
                    }

                    if (hasMoreNewParts && hasNewPartTask.IsCompleted) {
                        hasMoreNewParts = await hasNewPartTask;
                        while (hasMoreNewParts && _source.TryRead(out var part)) {
                            _readSourceParts.Add(part);
                            foreach (var stream in _streams)
                                await stream.Writer.WriteAsync(part, cancellationToken);
                        }
                    }
                }
            }
            finally {
                foreach (var stream in _streams)
                    stream.Writer.Complete();
                _streams.Clear();
            }
        }
    }
}
