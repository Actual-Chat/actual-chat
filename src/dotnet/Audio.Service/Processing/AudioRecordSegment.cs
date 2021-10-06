using ActualChat.Audio.WebM;
using ActualChat.Blobs;
using ActualChat.Channels;

namespace ActualChat.Audio.Processing;

public sealed class AudioRecordSegment
{
    private readonly WebMDocumentBuilder _webMBuilder;
    private readonly IReadOnlyList<AudioMetadataEntry> _metadata;
    private readonly double _offset;
    private readonly AsyncMemoizer<BlobPart> _distributor;
    private AudioStreamPart? _audioStreamPart;

    public StreamId StreamId { get; }
    public int Index { get; }
    public AudioRecord AudioRecord { get; }

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
        _distributor = source.Memoize();
    }

    public async Task<ChannelReader<BlobPart>> GetAudioStream()
    {
        var channel = Channel.CreateUnbounded<BlobPart>(
            new UnboundedChannelOptions {
                SingleWriter = true
            });
        await _distributor.AddReplayTarget(channel.Writer);
        return channel.Reader;
    }

    public async Task<AudioStreamPart> GetAudioStreamPart()
    {
        await _distributor.DistributeTask;
        return _audioStreamPart ??= new AudioStreamPart(
            Index,
            StreamId,
            AudioRecord,
            _webMBuilder.ToDocument(),
            _metadata,
            _offset,
            _metadata.Sum(md => md.Duration));
    }
}
