using ActualChat.Blobs;
using ActualChat.Redis;

namespace ActualChat.Audio;

public class SourceAudioRecorder : ISourceAudioRecorder, IAsyncDisposable
{
    private readonly IAuthService _auth;
    private readonly RedisDb _rootRedisDb;
    private readonly RedisDb _redisDb;
    private readonly RedisQueue<AudioRecord> _newRecordQueue;
    private readonly ILogger<SourceAudioRecorder> _log;

    public SourceAudioRecorder(
        IAuthService auth,
        RedisDb rootRedisDb,
        ILogger<SourceAudioRecorder> log)
    {
        _log = log;
        _auth = auth;
        _rootRedisDb = rootRedisDb;
        _redisDb = _rootRedisDb.WithKeyPrefix("source-audio");
        _newRecordQueue = _redisDb.GetQueue<AudioRecord>("new-records");
    }

    public ValueTask DisposeAsync()
        => _newRecordQueue.DisposeAsync();

    public async Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        audioRecord = audioRecord with {
            Id = new AudioRecordId(Ulid.NewUlid().ToString()),
            UserId = user.Id,
        };
        _log.LogInformation(nameof(RecordSourceAudio) + ": Record = {Record}", audioRecord);

        var streamer = _redisDb.GetStreamer<BlobPart>(audioRecord.Id);
        await streamer.Write(content,
            async _ => await _newRecordQueue.Enqueue(audioRecord),
            cancellationToken);
        _ = Task.Delay(TimeSpan.FromMinutes(1), default)
            .ContinueWith(_ => streamer.Remove(), TaskScheduler.Default);
    }

    public Task<AudioRecord> DequeueSourceAudio(CancellationToken cancellationToken)
        => _newRecordQueue.Dequeue(cancellationToken);

    public ChannelReader<BlobPart> GetSourceAudioStream(AudioRecordId audioRecordId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(audioRecordId);
        return streamer.Read(cancellationToken);
    }
}
