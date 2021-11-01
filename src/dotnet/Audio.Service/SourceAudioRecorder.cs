using ActualChat.Audio.Db;
using ActualChat.Blobs;
using ActualChat.Chat;
using ActualChat.Redis;
using ActualChat.Users;

namespace ActualChat.Audio;

public class SourceAudioRecorder : ISourceAudioRecorder, IAsyncDisposable
{
    private readonly IAuthService _auth;
    private readonly IAuthorService _authorService;
    private readonly ISessionInfoService _sessionInfoService;
    private readonly RedisDb _redisDb;
    private readonly RedisQueue<AudioRecord> _newRecordQueue;
    private readonly ILogger<SourceAudioRecorder> _log;
    private readonly ICommander _commander;

    public SourceAudioRecorder(
        ILogger<SourceAudioRecorder> log,
        ICommander commander,
        IAuthService auth,
        RedisDb<AudioDbContext> audioRedisDb,
        IAuthorService authorService,
        ISessionInfoService sessionInfoService)
    {
        _log = log;
        _commander = commander;
        _auth = auth;
        _redisDb = audioRedisDb.WithKeyPrefix("source-audio");
        _newRecordQueue = _redisDb.GetQueue<AudioRecord>("new-records");
        _authorService = authorService;
        _sessionInfoService = sessionInfoService;
    }

    public ValueTask DisposeAsync()
        => _newRecordQueue.DisposeAsync();

    public async Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        CancellationToken cancellationToken)
    {
        var authorId = await GetAuthorIdFromSession(session, audioRecord.ChatId, cancellationToken).ConfigureAwait(false)
            ?? await GetOrAddAuthorIdFromDatabase(session, audioRecord.ChatId, cancellationToken).ConfigureAwait(false);

        audioRecord = audioRecord with {
            Id = new AudioRecordId(Ulid.NewUlid().ToString()),
            AuthorId = authorId,
        };
        _log.LogInformation(nameof(RecordSourceAudio) + ": Record = {Record}", audioRecord);

        var streamer = _redisDb.GetStreamer<BlobPart>(audioRecord.Id);
        await streamer.Write(content,
            async _ => await _newRecordQueue.Enqueue(audioRecord).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        _ = Task.Delay(TimeSpan.FromMinutes(1), default)
            .ContinueWith(_ => streamer.Remove(), TaskScheduler.Default);

        // TODO: move this under an abstraction like IAuthorIdAccessor
        async Task<string?> GetAuthorIdFromSession(Session session, ChatId chatId, CancellationToken cancellationToken)
        {
            var sessionInfo = await _auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
            return sessionInfo.Options[$"{chatId}::authorId"] as string;
        }

        async Task<string> GetOrAddAuthorIdFromDatabase(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
        {
            var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
            var author = await _authorService.GetByUserIdAndChatId(user.Id, chatId, cancellationToken).ConfigureAwait(false);
            var authorId = author?.AuthorId ?? AuthorId.None;
            var userId = user.IsAuthenticated ? user.Id : UserId.None;
            if (authorId.IsNone) {
                authorId = await _authorService.CreateAuthor(new(userId, chatId), cancellationToken).ConfigureAwait(false);
            }
            await _commander.Call(
                new ISessionInfoService.UpsertCommand(session, new($"{chatId}::authorId", authorId)),
                isolate: true,
                cancellationToken
            ).ConfigureAwait(false);
            return authorId.ToString();
        }
    }

    public Task<AudioRecord> DequeueSourceAudio(CancellationToken cancellationToken)
        => _newRecordQueue.Dequeue(cancellationToken);

    public ChannelReader<BlobPart> GetSourceAudioStream(AudioRecordId audioRecordId, CancellationToken cancellationToken)
    {
        var streamer = _redisDb.GetStreamer<BlobPart>(audioRecordId);
        return streamer.Read(cancellationToken);
    }
}
