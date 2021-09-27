using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio
{
    public class AudioRecorder : IAudioRecorder
    {
        private readonly AudioRecordProducer.Options _producerSetup;
        private readonly ILogger<AudioRecorder> _log;
        private readonly IConnectionMultiplexer _redis;
        private readonly IAuthService _auth;

        public AudioRecorder(
            AudioRecordProducer.Options producerSetup,
            IConnectionMultiplexer redis,
            IAuthService auth,
            ILogger<AudioRecorder> log)
        {
            _log = log;
            _producerSetup = producerSetup;
            _redis = redis;
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

            var db = _producerSetup.GetDatabase(_redis);
            var contentQueue = _producerSetup.GetContentQueue(db);
            var partStreamer = _producerSetup.GetPartStreamer(db, record.Id);
            await partStreamer.Write(content,
                async _ => await contentQueue.Enqueue(record),
                cancellationToken);
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => partStreamer.Remove(), CancellationToken.None);
        }
    }
}
