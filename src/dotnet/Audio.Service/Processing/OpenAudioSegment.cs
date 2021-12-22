
namespace ActualChat.Audio.Processing;

public sealed class OpenAudioSegment
{
    public static string GetStreamId(string audioRecordId, int index)
        => $"{audioRecordId}-{index:D4}";

    public int Index { get; }
    public string StreamId { get; }
    public AudioRecord AudioRecord { get; }
    public AudioSource Audio { get; }
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
        ClosedSegmentTask = TaskSource.New<ClosedAudioSegment>(true).Task;
    }

    public void Close(TimeSpan duration)
    {
        var audioSegment = new ClosedAudioSegment(this, duration);
        TaskSource.For(ClosedSegmentTask).SetResult(audioSegment);
    }

    public void TryClose(TimeSpan duration)
    {
        var audioSegment = new ClosedAudioSegment(this, duration);
        TaskSource.For(ClosedSegmentTask).TrySetResult(audioSegment);
    }

    public void TryClose(Exception error)
        => TaskSource.For(ClosedSegmentTask).SetException(error);
}
