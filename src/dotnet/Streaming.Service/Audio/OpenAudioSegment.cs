using ActualChat.Audio;

namespace ActualChat.Streaming;

public sealed class OpenAudioSegment(
    int index,
    AudioRecord record,
    AudioSource source,
    Author author,
    AudioSegmentLanguage languages,
    ILogger log)
{
    private readonly AsyncTaskMethodBuilder<Moment?> _recordedAtSource = AsyncTaskMethodBuilderExt.New<Moment?>();
    private readonly AsyncTaskMethodBuilder<TimeSpan> _audibleDurationSource = AsyncTaskMethodBuilderExt.New<TimeSpan>();
    private readonly AsyncTaskMethodBuilder<ClosedAudioSegment> _closedSegmentSource = AsyncTaskMethodBuilderExt.New<ClosedAudioSegment>();

    public int Index { get; } = index;
    public StreamId StreamId { get; } = GetStreamId(record, index);
    public AudioRecord Record { get; } = record;
    public AudioSource Source { get; } = source;
    public Author Author { get; } = author;
    public AudioSegmentLanguage Languages { get; } = languages;
    public Task<Moment?> RecordedAt => _recordedAtSource.Task;
    public Task<TimeSpan> AudibleDuration => _audibleDurationSource.Task;
    public Task<ClosedAudioSegment> ClosedSegment => _closedSegmentSource.Task;
    private ILogger Log { get; } = log;

    public static StreamId GetStreamId(AudioRecord record, int index)
    {
        var streamId = record.StreamId;
        return StreamId.New(streamId.NodeRef, $"{streamId.LocalId}-{index:D4}");
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
        // ReSharper disable once PossiblyImpureMethodCallOnReadonlyVariable
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
