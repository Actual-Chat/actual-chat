
namespace ActualChat.Audio.Processing;

public sealed class AudioRecordSegment
{
    private readonly Task<TimeSpan> _durationTask;
    private AudioStreamPart? _audioStreamPart;

    public int Index { get; }
    public StreamId StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Source { get; }
    public TimeSpan Offset { get; }

    public AudioRecordSegment(
        int index,
        AudioRecord audioRecord,
        AudioSource source,
        TimeSpan offset,
        Task<TimeSpan> durationTask)
    {
        _durationTask = durationTask;
        Index = index;
        StreamId = new StreamId(audioRecord.Id, index);
        AudioRecord = audioRecord;
        Source = source;
        Offset = offset;
    }

    public async Task<AudioStreamPart> GetAudioStreamPart()
    {
        var duration = await _durationTask.ConfigureAwait(false);
        return _audioStreamPart ??= new AudioStreamPart(
            Index,
            StreamId,
            AudioRecord,
            Source,
            Offset,
            duration);
    }
}
