using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance.Buffers;
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
            AudioRecord upload,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToken)
        {
            var user = await _auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();

            var recordId = new AudioRecordId(Ulid.NewUlid().ToString());
            _log.LogInformation("Uploading: RecordId = {RecordId}", (string) recordId);

            var firstCycle = true;
            var db = _producerSetup.GetDatabase(_redis);

            var streamKey = _producerSetup.StreamKeyProvider(recordId);
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

                    await db.StreamAddAsync(streamKey, _producerSetup.PartKey, serialized,
                        maxLength: 1000, useApproximateMaxLength: true);
                }

                if (firstCycle) {
                    firstCycle = false;
                    _ = NotifyNewAudioRecord(db, upload, cancellationToken);
                }
                _ = NotifyNewAudioPart(recordId);
            }
            if (firstCycle) {
                // We'll land here if there where no audio parts
                firstCycle = false;
                _ = NotifyNewAudioRecord(db, upload, cancellationToken);
            }

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await db.StreamAddAsync(streamKey, _producerSetup.StatusKey, _producerSetup.CompletedStatus,
                maxLength: 1000, useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(streamKey), CancellationToken.None);
        }

        private async Task NotifyNewAudioRecord(IDatabase db, AudioRecord audioRecord, CancellationToken cancellationToken)
        {
            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, audioRecord, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;
            db.ListLeftPush(_producerSetup.NewContentNewsChannelName, serialized);

            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(_producerSetup.NewContentNewsChannelName, string.Empty);
        }

        private async Task NotifyNewAudioPart(AudioRecordId recordId)
        {
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(_producerSetup.NewPartNewsChannelKeyProvider(recordId), string.Empty);
        }
    }
}
