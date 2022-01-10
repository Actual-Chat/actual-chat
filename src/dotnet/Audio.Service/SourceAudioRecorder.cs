using ActualChat.Audio.Db;
using ActualChat.Media;
using Stl.Redis;

namespace ActualChat.Audio;

public class SourceAudioRecorder : ISourceAudioRecorder, IAsyncDisposable
{
    private RedisQueue<AudioRecord>? _newRecordQueue = null!;

    protected ILogger<SourceAudioRecorder> Log { get; }
    protected bool DebugMode => Constants.DebugMode.AudioProcessing;
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected object Lock { get; } = new();

    protected RedisDb RedisDb { get; }

    protected RedisQueue<AudioRecord> NewRecordQueue {
        get {
            lock (Lock) {
                return _newRecordQueue ??= RedisDb.GetQueue<AudioRecord>("new-records", new() {
                    EnqueueCheckPeriod = TimeSpan.FromMilliseconds(250),
                });
            }
        }
    }

    protected MomentClockSet Clocks { get; }

    public SourceAudioRecorder(
        RedisDb<AudioContext> audioRedisDb,
        MomentClockSet clocks,
        ILogger<SourceAudioRecorder> log)
    {
        Log = log;
        Clocks = clocks;
        RedisDb = audioRedisDb.WithKeyPrefix("source-audio");
        _ = NewRecordQueue;
    }

    public ValueTask DisposeAsync()
    {
        RecycleNewRecordQueue();
        return ValueTask.CompletedTask;
    }

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

        await streamer.Write(recordingStream, AnnounceNewRecord, cancellationToken).ConfigureAwait(false);
        _ = BackgroundTask.Run(DelayedStreamerRemoval,
            Log, $"{nameof(DelayedStreamerRemoval)} failed",
            CancellationToken.None);

        async ValueTask AnnounceNewRecord(RedisStreamer<RecordingPart> _)
        {
            try {
                await NewRecordQueue.Enqueue(record).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "RecordSourceAudio.AnnounceNewRecord failed");
                RecycleNewRecordQueue();
            }
        }

        async Task DelayedStreamerRemoval()
        {
            await Clocks.CpuClock.Delay(TimeSpan.FromMinutes(1), CancellationToken.None).ConfigureAwait(false);
            await streamer.Remove().ConfigureAwait(false);
        }
    }

    public Task<AudioRecord> DequeueSourceAudio(CancellationToken cancellationToken)
    {
        try {
            return NewRecordQueue.Dequeue(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "DequeueSourceAudio failed");
            RecycleNewRecordQueue();
            throw;
        }
    }

    public IAsyncEnumerable<RecordingPart> GetSourceAudioRecordingStream(string audioRecordId, CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<RecordingPart>(audioRecordId, new() {
            AppendCheckPeriod = TimeSpan.FromMilliseconds(250),
        });
        // streamer.Log = DebugLog;
        return streamer.Read(cancellationToken).Memoize().Replay(cancellationToken);
    }

    // Protected methods

    protected void RecycleNewRecordQueue()
    {
        RedisQueue<AudioRecord>? newRecordQueue;
        lock (Lock) {
            newRecordQueue = _newRecordQueue;
            _newRecordQueue = null;
        }
        newRecordQueue?.DisposeAsync();
    }
}
