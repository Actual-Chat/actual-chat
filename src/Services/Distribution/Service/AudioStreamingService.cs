using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Async;
using Stl.Generators;
using Stl.Text;

namespace ActualChat.Distribution
{
    public class AudioStreamingService : StreamingService<AudioMessage>, IAudioStreamingService
    {
        private readonly ILogger<AudioStreamingService> _log;
        private readonly RandomStringGenerator _idGenerator;

        public AudioStreamingService(IConnectionMultiplexer redis, ILogger<AudioStreamingService> log) : base(redis)
        {
            _log = log;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
        }

        public async Task<RecordingId> UploadRecording(AudioRecordingConfiguration config, ChannelReader<AudioMessage> source, CancellationToken cancellationToken)
        {
            var recordingId = _idGenerator.Next();
            _log.LogInformation($"{nameof(UploadRecording)}, RecordingId = {{RecordingId}}", recordingId);
            
            var db = GetDatabase();
            var key = new RedisKey(recordingId);

            // TODO(AK): lack of await \ error handling and CancellationToken
            _ = InternalUpload(db, key, source);

            await NotifyNewAudioRecording(db, recordingId, config, cancellationToken);

            return recordingId;
        }

        private async Task NotifyNewAudioRecording(IDatabase db, string recordingId, AudioRecordingConfiguration config, CancellationToken cancellationToken)
        {
            var recording = new AudioRecording(recordingId, config);
            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, recording, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;
            db.ListLeftPush(StreamingConstants.AudioRecordingQueue, serialized);
            
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(StreamingConstants.AudioRecordingQueue, string.Empty);
        }

        private async Task InternalUpload(IDatabase db, RedisKey key, ChannelReader<AudioMessage> source)
        {
            while (await source.WaitToReadAsync())
            while (source.TryRead(out var message)) {
                using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard);
                var serialized = bufferWriter.WrittenMemory;

                await db.StreamAddAsync(
                    key,
                  StreamingConstants.MessageKey,
                  serialized,
                    maxLength: 1000,
                    useApproximateMaxLength: true);
            }

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await db.StreamAddAsync(
                key,
              StreamingConstants.StatusKey,
                StreamingConstants.Completed,
                maxLength: 1000,
                useApproximateMaxLength: true);
            
            // TODO(AK): AY please review
            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(key), CancellationToken.None)
                .Ignore();
        }


        protected override IDatabase GetDatabase() 
            => Redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);
    }
}