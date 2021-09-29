using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio
{
    public class AudioRecorder : IAudioRecorder
    {
        private readonly AudioRecordProducer.Options _producerSetup;
        private readonly ILogger<AudioRecorder> _log;
        private readonly RedisDb _rootRedisDb;
        private readonly IAuthService _auth;

        public AudioRecorder(
            AudioRecordProducer.Options producerSetup,
            RedisDb rootRedisDb,
            IAuthService auth,
            ILogger<AudioRecorder> log)
        {
            _log = log;
            _producerSetup = producerSetup;
            _rootRedisDb = rootRedisDb;
            _auth = auth;
        }

        public async Task Record(
            Session session,
            AudioRecord record,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToken)
        {
            var user = await _auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();

            record = record with {
                Id = new AudioRecordId(Ulid.NewUlid().ToString()),
                UserId = user.Id,
            };
            _log.LogInformation("Uploading: Record = {Record}", record);

            var redisDb = _rootRedisDb.WithKeyPrefix(_producerSetup.KeyPrefix);
            var contentQueue = _producerSetup.GetContentQueue(redisDb);
            var partStreamer = _producerSetup.GetPartStreamer(redisDb, record.Id);
            await partStreamer.Write(content,
                async _ => await contentQueue.Enqueue(record),
                cancellationToken);
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => partStreamer.Remove(), CancellationToken.None);
        }
    }
}
