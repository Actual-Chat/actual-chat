using ActualChat.Audio.Db;
using ActualChat.Chat;
using ActualChat.Media;
using Stl.Redis;

namespace ActualChat.Audio;

public class SourceAudioRecorder : ISourceAudioRecorder, IAsyncDisposable
{
    protected ILogger<SourceAudioRecorder> Log { get; }
    protected bool DebugMode => Constants.DebugMode.AudioProcessing;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    private RedisDb RedisDb { get; }
    private RedisQueue<AudioRecord> NewRecordQueue { get; }
    private MomentClockSet Clocks { get; }

    public SourceAudioRecorder(
        RedisDb<AudioContext> audioRedisDb,
        MomentClockSet clocks,
        ILogger<SourceAudioRecorder> log)
    {
        Log = log;
        Clocks = clocks;
        RedisDb = audioRedisDb.WithKeyPrefix("source-audio");
        NewRecordQueue = RedisDb.GetQueue<AudioRecord>("new-records", new() {
            EnqueueCheckPeriod = TimeSpan.FromMilliseconds(250),
        });
    }

    public ValueTask DisposeAsync()
        => NewRecordQueue.DisposeAsync();

    public async Task RecordSourceAudio(
        Session session,
        AudioRecord record,
        IAsyncEnumerable<RecordingPart> recordingStream,
        CancellationToken cancellationToken)
    {
        Log.LogInformation("RecordSourceAudio: Record = {Record}", record);
        record = record with { Id = new string(Ulid.NewUlid().ToString()) };

        var streamer = RedisDb.GetStreamer<RecordingPart>(record.Id);
        // streamer.Log = DebugLog;
        if (Constants.DebugMode.AudioRecordingStream)
            recordingStream = recordingStream.WithLog(Log, "RecordSourceAudio", cancellationToken);
        await streamer.Write(
                recordingStream,
                _ => NewRecordQueue.Enqueue(record).ToValueTask(),
                cancellationToken)
            .ConfigureAwait(false);
        _ = BackgroundTask.Run(DelayedStreamerRemoval,
            Log, $"{nameof(DelayedStreamerRemoval)} failed",
            CancellationToken.None);

        async Task DelayedStreamerRemoval()
        {
            await Clocks.CpuClock.Delay(TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);
            await streamer.Remove().ConfigureAwait(false);
        }
    }

    public Task<AudioRecord> DequeueSourceAudio(CancellationToken cancellationToken)
        => NewRecordQueue.Dequeue(cancellationToken);

    public IAsyncEnumerable<RecordingPart> GetSourceAudioRecordingStream(string audioRecordId, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<RecordingPart>(audioRecordId, new() {
            AppendCheckPeriod = TimeSpan.FromMilliseconds(250),
        });
        // streamer.Log = DebugLog;
        return streamer.Read(cancellationToken).Memoize().Replay(cancellationToken);
    }
}
