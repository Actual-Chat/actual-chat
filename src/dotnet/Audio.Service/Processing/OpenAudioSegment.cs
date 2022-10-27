
using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Audio.Processing;

public sealed class OpenAudioSegment
{
    public static string GetStreamId(string audioRecordId, int index)
        => $"{audioRecordId}-{index:D4}";

    public int Index { get; }
    public string StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Audio { get; }
    public ChatAuthor Author { get; }
    public ImmutableArray<LanguageId> Languages { get; }
    public Task<Moment?> RecordedAtTask { get; }
    public Task<TimeSpan> AudibleDurationTask { get; }
    public Task<ClosedAudioSegment> ClosedSegmentTask { get; }
    private ILogger Log { get; }

    public OpenAudioSegment(
        int index,
        AudioRecord audioRecord,
        AudioSource audio,
        ChatAuthor author,
        ImmutableArray<LanguageId> languages,
        ILogger log)
    {
        Log = log;
        Index = index;
        StreamId = GetStreamId(audioRecord.Id, index);
        AudioRecord = audioRecord;
        Audio = audio;
        Author = author;
        Languages = languages;
        RecordedAtTask = TaskSource.New<Moment?>(true).Task;
        AudibleDurationTask = TaskSource.New<TimeSpan>(true).Task;
        ClosedSegmentTask = TaskSource.New<ClosedAudioSegment>(true).Task;
    }

    public void SetRecordedAt(Moment? recordedAt)
    {
        if (!TaskSource.For(RecordedAtTask).TrySetResult(recordedAt))
            Log.LogWarning(
                "SetRecordedAt came too late for OpenAudioSegment #{Index} of Stream #{StreamId}",
                Index, StreamId);
    }

    // TODO(AK): review: use or delete
    public void SetAudibleDuration(TimeSpan audibleDuration)
    {
        if (!TaskSource.For(AudibleDurationTask).TrySetResult(audibleDuration))
            Log.LogWarning(
                "SetAudibleDuration came too late for OpenAudioSegment #{Index} of Stream #{StreamId}",
                Index, StreamId);
    }

    public void Close(TimeSpan duration)
    {
        TaskSource.For(RecordedAtTask).TrySetResult(null);
        TaskSource.For(AudibleDurationTask).TrySetResult(duration);

        var recordedAt = RecordedAtTask.ToResultSynchronously().IsValue(out var r) ? r : null;
        var audibleDuration = AudibleDurationTask.ToResultSynchronously().IsValue(out var d) ? d : duration;
        var audioSegment = new ClosedAudioSegment(this, recordedAt, duration, audibleDuration);
        TaskSource.For(ClosedSegmentTask).SetResult(audioSegment);
    }

    // TODO(AK): review: use or delete
    public void TryClose(Exception error)
    {
        TaskSource.For(RecordedAtTask).TrySetException(error);
        TaskSource.For(AudibleDurationTask).TrySetException(error);
        TaskSource.For(ClosedSegmentTask).TrySetException(error);
    }
}

