
namespace ActualChat.Audio.Processing;

#pragma warning disable VSTHRD002
public sealed class OpenAudioSegment
{
    private readonly Task<TimeSpan> _startSegmentOffset;

    public static string GetStreamId(string audioRecordId, int index)
        => $"{audioRecordId}-{index:D4}";

    public int Index { get; }
    public string StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Audio { get; }
    public Task<ClosedAudioSegment> ClosedSegmentTask { get; }
    public Task<Moment> RecordedAtTask { get; }
    public Task<TimeSpan?> VoiceDurationTask { get; }

    public OpenAudioSegment(
        int index,
        AudioRecord audioRecord,
        AudioSource audio)
    {
        _startSegmentOffset = TaskSource.New<TimeSpan>(true).Task;
        Index = index;
        StreamId = GetStreamId(audioRecord.Id, index);
        AudioRecord = audioRecord;
        Audio = audio;
        ClosedSegmentTask = TaskSource.New<ClosedAudioSegment>(true).Task;
        RecordedAtTask = TaskSource.New<Moment>(true).Task;
        VoiceDurationTask = TaskSource.New<TimeSpan?>(true).Task;
    }

    public void Close(TimeSpan duration)
    {
        var recordedAt = RecordedAtTask.IsCompletedSuccessfully
            ? RecordedAtTask.Result
            : (Moment?)null;
        var voiceDuration = VoiceDurationTask.IsCompletedSuccessfully
            ? VoiceDurationTask.Result
            : null;
        var audioSegment = new ClosedAudioSegment(this, recordedAt, duration, voiceDuration);
        TaskSource.For(ClosedSegmentTask).SetResult(audioSegment);
    }
    public void TryClose(Exception error)
        => TaskSource.For(ClosedSegmentTask).SetException(error);

    public void SetRecordedAt(Moment recordedAt, TimeSpan? offset)
    {
        TaskSource.For(RecordedAtTask).SetResult(recordedAt);
        if (offset.HasValue)
            TaskSource.For(_startSegmentOffset).SetResult(offset.Value);
    }

    public void SetSilenceOffset(TimeSpan offset)
    {
        if (_startSegmentOffset.IsCompletedSuccessfully) {
            var startOffset = _startSegmentOffset.Result;
            TaskSource.For(VoiceDurationTask).SetResult(offset - startOffset);
        }
        else
            TaskSource.For(VoiceDurationTask).SetResult(null);

    }
}
#pragma warning restore VSTHRD002

