
namespace ActualChat.Audio.Processing;

public sealed class OpenAudioSegment
{
    public static string GetStreamId(string audioRecordId, int index)
        => $"{audioRecordId}-{index:D4}";

    public int Index { get; }
    public string StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Audio { get; }
    public Task<Moment?> RecordedAtTask { get; }
    public Task<TimeSpan> AudibleDurationTask { get; }
    public Task<ClosedAudioSegment> ClosedSegmentTask { get; }

    public OpenAudioSegment(
        int index,
        AudioRecord audioRecord,
        AudioSource audio)
    {
        Index = index;
        StreamId = GetStreamId(audioRecord.Id, index);
        AudioRecord = audioRecord;
        Audio = audio;
        RecordedAtTask = TaskSource.New<Moment?>(true).Task;
        AudibleDurationTask = TaskSource.New<TimeSpan>(true).Task;
        ClosedSegmentTask = TaskSource.New<ClosedAudioSegment>(true).Task;
    }

    public void SetRecordedAt(Moment? recordedAt)
        => TaskSource.For(RecordedAtTask).SetResult(recordedAt);

    public void SetAudibleDuration(TimeSpan audibleDuration)
        => TaskSource.For(AudibleDurationTask).SetResult(audibleDuration);

    public void Close(TimeSpan duration)
    {
        TaskSource.For(RecordedAtTask).TrySetResult(null);
        TaskSource.For(AudibleDurationTask).TrySetResult(duration);

        var recordedAt = RecordedAtTask.ToResult().IsValue(out var r) ? r : null;
        var audibleDuration = AudibleDurationTask.ToResult().IsValue(out var d) ? d : duration;
        var audioSegment = new ClosedAudioSegment(this, recordedAt, duration, audibleDuration);
        TaskSource.For(ClosedSegmentTask).SetResult(audioSegment);
    }

    public void TryClose(Exception error)
    {
        TaskSource.For(RecordedAtTask).TrySetException(error);
        TaskSource.For(AudibleDurationTask).TrySetException(error);
        TaskSource.For(ClosedSegmentTask).TrySetException(error);
    }
}

