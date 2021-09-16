using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Fusion.Authentication;
using Stl.Generators;

namespace ActualChat.Streaming
{
    public class AudioUploader : IAudioUploader
    {
        private readonly ILogger<AudioUploader> _log;
        private readonly RandomStringGenerator _idGenerator;
        private readonly IConnectionMultiplexer _redis;
        private readonly IAuthService _auth;

        public AudioUploader(IConnectionMultiplexer redis, IAuthService auth, ILogger<AudioUploader> log)
        {
            _redis = redis;
            _auth = auth;
            _log = log;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
        }

        public async Task Upload(Session session, AudioRecord upload, ChannelReader<BlobPart> content, CancellationToken cancellationToken)
        {
            var user = await _auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();

            var recordId = new AudioRecordId(_idGenerator.Next());
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
                StreamingConstants.Completed,
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
            db.ListLeftPush(StreamingConstants.AudioRecordingQueue, serialized);

            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(StreamingConstants.AudioRecordingQueue, string.Empty);
        }

        private async Task NotifyNewAudioMessage(AudioRecordId audioRecordId)
        {
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(audioRecordId.GetRedisChannelName(),string.Empty);
        }

        protected IDatabase GetDatabase()
            => _redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);
    }
}
