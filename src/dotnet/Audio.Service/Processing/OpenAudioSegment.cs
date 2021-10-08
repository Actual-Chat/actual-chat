
namespace ActualChat.Audio.Processing;

public sealed class OpenAudioSegment
{
    private AudioSegment? _audioSegment;

    public int Index { get; }
    public StreamId StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Source { get; }
    public TimeSpan Offset { get; }
    public Task<TimeSpan> DurationTask { get; }

    public OpenAudioSegment(
        int index,
        AudioRecord audioRecord,
        AudioSource source,
        TimeSpan offset,
        Task<TimeSpan> durationTask)
    {
        Index = index;
        StreamId = new StreamId(audioRecord.Id, index);
        AudioRecord = audioRecord;
        Source = source;
        Offset = offset;
        DurationTask = durationTask;
    }

    public async Task<AudioSegment> Close()
        => _audioSegment ??= new AudioSegment(
            Index, StreamId, AudioRecord, Source, Offset,
            await DurationTask.ConfigureAwait(false));
}
