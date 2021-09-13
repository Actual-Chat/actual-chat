using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Generators;

namespace ActualChat.Streaming
{
    public class AudioRecordingService : IAudioRecordingService
    {
        private readonly ILogger<AudioRecordingService> _log;
        private readonly RandomStringGenerator _idGenerator;
        private readonly IConnectionMultiplexer _redis;

        public AudioRecordingService(IConnectionMultiplexer redis, ILogger<AudioRecordingService> log) 
        {
            _redis = redis;
            _log = log;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
        }

        public async Task UploadRecording(AudioRecordingConfiguration config, ChannelReader<BlobMessage> source, CancellationToken cancellationToken)
        {
            var recordingId = _idGenerator.Next();
            _log.LogInformation($"{nameof(UploadRecording)}, RecordingId = {{RecordingId}}", recordingId);

            var firstCycle = true;
            var db = GetDatabase();
            var key = new RedisKey(recordingId);

            while (await source.WaitToReadAsync(cancellationToken)) {
                while (source.TryRead(out var message)) {
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
                    _ = NotifyNewAudioRecording(db, recordingId, config, cancellationToken);
                }
                _ = NotifyNewAudioMessage(recordingId);
            }
            if (firstCycle) _ = NotifyNewAudioRecording(db, recordingId, config, cancellationToken);

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

        private async Task NotifyNewAudioRecording(IDatabase db, RecordingId recordingId, AudioRecordingConfiguration config, CancellationToken cancellationToken)
        {
            var recording = new AudioRecording(recordingId, config);
            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, recording, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;
            db.ListLeftPush(StreamingConstants.AudioRecordingQueue, serialized);

            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(StreamingConstants.AudioRecordingQueue, string.Empty);
        }

        private async Task NotifyNewAudioMessage(string recordingId)
        {
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(IdExtensions.GetChannelName(new RecordingId(recordingId)),string.Empty);
        }


        protected IDatabase GetDatabase()
            => _redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);
    }
}