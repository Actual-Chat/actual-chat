using ActualChat.Chat;

namespace ActualChat.Audio.Processing;

public sealed class OpenAudioSegment
{
    public static string GetStreamId(string audioRecordId, int index)
        => $"{audioRecordId}-{index.ToString("D4", CultureInfo.InvariantCulture)}";

    private TaskCompletionSource<Moment?> _recordedAtSource = TaskCompletionSourceExt.New<Moment?>();
    private TaskCompletionSource<TimeSpan> _audibleDurationSource = TaskCompletionSourceExt.New<TimeSpan>();
    private TaskCompletionSource<ClosedAudioSegment> _closedSegmentSource = TaskCompletionSourceExt.New<ClosedAudioSegment>();

    public int Index { get; }
    public string StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Audio { get; }
    public Author Author { get; }
    public ApiArray<Language> Languages { get; }
    public Task<Moment?> RecordedAt => _recordedAtSource.Task;
    public Task<TimeSpan> AudibleDuration => _audibleDurationSource.Task;
    public Task<ClosedAudioSegment> ClosedSegment => _closedSegmentSource.Task;
    private ILogger Log { get; }

    public OpenAudioSegment(
        int index,
        AudioRecord audioRecord,
        AudioSource audio,
        Author author,
        ApiArray<Language> languages,
        ILogger log)
    {
        Log = log;
        Index = index;
        StreamId = GetStreamId(audioRecord.Id, index);
        AudioRecord = audioRecord;
        Audio = audio;
        Author = author;
        Languages = languages;
    }

    public void SetRecordedAt(Moment? recordedAt)
    {
        if (!_recordedAtSource.TrySetResult(recordedAt))
            Log.LogWarning(
                "SetRecordedAt came too late for OpenAudioSegment #{Index} of Stream #{StreamId}",
                Index, StreamId);
    }

    // TODO(AK): review: use or delete
    public void SetAudibleDuration(TimeSpan audibleDuration)
    {
        if (!_audibleDurationSource.TrySetResult(audibleDuration))
            Log.LogWarning(
                "SetAudibleDuration came too late for OpenAudioSegment #{Index} of Stream #{StreamId}",
                Index, StreamId);
    }

    public void Close(TimeSpan duration)
    {
        _recordedAtSource.TrySetResult(null);
        _audibleDurationSource.TrySetResult(duration);

        var recordedAt = RecordedAt.ToResultSynchronously().IsValue(out var r) ? r : null;
        var audibleDuration = AudibleDuration.ToResultSynchronously().IsValue(out var d) ? d : duration;
        var audioSegment = new ClosedAudioSegment(this, recordedAt, duration, audibleDuration);
        _closedSegmentSource.SetResult(audioSegment);
    }

    // TODO(AK): review: use or delete
    public void TryClose(Exception error)
    {
        _recordedAtSource.TrySetException(error);
        _audibleDurationSource.TrySetException(error);
        _closedSegmentSource.TrySetException(error);
    }
}
