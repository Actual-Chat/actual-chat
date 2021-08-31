using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Async;
using Stl.Text;

namespace ActualChat.Distribution
{
    public class AudioStreamingService : StreamingService<AudioMessage>, IAudioStreamingService
    {
        public AudioStreamingService(IConnectionMultiplexer redis) : base(redis)
        {
        }

        public async Task UploadStream(Symbol recordingId, ChannelReader<AudioMessage> source, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(recordingId);
            
            while (await source.WaitToReadAsync(cancellationToken))
            while (source.TryRead(out var message)) {
                using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard, cancellationToken);
                var serialized = bufferWriter.WrittenMemory;

                await db.StreamAddAsync(key, StreamingConstants.MessageKey, serialized, maxLength: 1000, useApproximateMaxLength: true);
            }

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await db.StreamAddAsync(key, StreamingConstants.StatusKey,  StreamingConstants.Completed, maxLength: 1000, useApproximateMaxLength: true);

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