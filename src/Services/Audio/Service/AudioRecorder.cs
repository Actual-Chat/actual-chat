using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using ActualChat.Streaming.Server;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Fusion.Authentication;
using Stl.Generators;

namespace ActualChat.Audio
{
    public class AudioRecorder : IAudioRecorder
    {
        private readonly ILogger<AudioRecorder> _log;
        private readonly IConnectionMultiplexer _redis;
        private readonly IAuthService _auth;
        private readonly IIdentifierGenerator<AudioRecordId> _idGenerator;

        public AudioRecorder(
            IConnectionMultiplexer redis,
            IAuthService auth,
            IIdentifierGenerator<AudioRecordId> idGenerator,
            ILogger<AudioRecorder> log)
        {
            _redis = redis;
            _auth = auth;
            _idGenerator = idGenerator;
            _log = log;
        }

        public async Task Record(
            Session session,
            AudioRecord upload,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToken)
        {
            var user = await _auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();

            var recordId = _idGenerator.Next();
            _log.LogInformation("Uploading: RecordId = {RecordId}", (string) recordId);

            var firstCycle = true;
            var db = GetDatabase();
            var key = new RedisKey(recordId);
            upload = upload with {
                Id = recordId,
                UserId = user.Id,
            };

            while (await content.WaitToReadAsync(cancellationToken)) {
                while (content.TryRead(out var message)) {
                    using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                    MessagePackSerializer.Serialize(
                        bufferWriter,
                        message,
                        MessagePackSerializerOptions.Standard,
                        cancellationToken);
                    var serialized = bufferWriter.WrittenMemory;

                    await db.StreamAddAsync(
                        key,
                        StreamingConstants.MessageKey,
                        serialized,
                        maxLength: 1000,
                        useApproximateMaxLength: true);
                }

                if (firstCycle) {
                    firstCycle = false;
                    _ = NotifyNewAudioRecording(db, upload, cancellationToken);
                }
                _ = NotifyNewAudioMessage(recordId);
            }
            if (firstCycle) _ = NotifyNewAudioRecording(db, upload, cancellationToken);

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await db.StreamAddAsync(
                key,
                StreamingConstants.StatusKey,
                StreamingConstants.CompletedStatus,
                maxLength: 1000,
                useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(key), CancellationToken.None);
        }

        private async Task NotifyNewAudioRecording(IDatabase db, AudioRecord audioRecord, CancellationToken cancellationToken)
        {
            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, audioRecord, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;
            db.ListLeftPush("queue", serialized);

            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync("queue", string.Empty);
        }

        private async Task NotifyNewAudioMessage(AudioRecordId audioRecordId)
        {
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(audioRecordId.GetRedisChannelName(),string.Empty);
        }

        protected IDatabase GetDatabase()
            => _redis.GetDatabase().WithKeyPrefix("audio");
    }
}
