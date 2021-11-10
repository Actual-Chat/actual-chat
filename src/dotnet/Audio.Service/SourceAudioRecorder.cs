using ActualChat.Audio.Db;
using ActualChat.Blobs;
using ActualChat.Chat;
using ActualChat.Redis;
using Stl.Redis;

namespace ActualChat.Audio;

public class SourceAudioRecorder : ISourceAudioRecorder, IAsyncDisposable
{
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly RedisDb _redisDb;
    private readonly RedisQueue<AudioRecord> _newRecordQueue;
    private readonly ILogger<SourceAudioRecorder> _log;

    public SourceAudioRecorder(
        ILogger<SourceAudioRecorder> log,
        RedisDb<AudioContext> audioRedisDb,
        IChatAuthorsBackend chatAuthorsBackend)
    {
        _log = log;
        _redisDb = audioRedisDb.WithKeyPrefix("source-audio");
        _newRecordQueue = _redisDb.GetQueue<AudioRecord>("new-records");
        _chatAuthorsBackend = chatAuthorsBackend;
    }

    public ValueTask DisposeAsync()
        => _newRecordQueue.DisposeAsync();

    public async Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobStream,
        CancellationToken cancellationToken)
    {
        var author = await _chatAuthorsBackend.GetOrCreate(session, audioRecord.ChatId, cancellationToken).ConfigureAwait(false);
        audioRecord = audioRecord with {
            Id = new AudioRecordId(Ulid.NewUlid().ToString()),
            AuthorId = author.Id,
        };
        _log.LogInformation(nameof(RecordSourceAudio) + ": Record = {Record}", audioRecord);

        var streamer = _redisDb.GetStreamer<BlobPart>(audioRecord.Id);
        await streamer.Write(
                blobStream,
                async _ => await _newRecordQueue.Enqueue(audioRecord).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);
        _ = Task.Delay(TimeSpan.FromMinutes(1), default)
            .ContinueWith(_ => streamer.Remove(), TaskScheduler.Default);
    }

    public Task<AudioRecord> DequeueSourceAudio(CancellationToken cancellationToken)
        => _newRecordQueue.Dequeue(cancellationToken);

    public IAsyncEnumerable<BlobPart> GetSourceAudioBlobStream(AudioRecordId audioRecordId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(audioRecordId);
        return streamer.Read(cancellationToken);
    }
}
