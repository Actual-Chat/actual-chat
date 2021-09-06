using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Distribution
{
    public class ServerSideAudioStreamingService : ServerSideStreamingService<AudioMessage>, IServerSideAudioStreamingService
    {
        public ServerSideAudioStreamingService(IConnectionMultiplexer redis) : base(redis)
        {
        }

        public async Task<AudioRecording?> WaitForNewRecording(CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var subscriber = Redis.GetSubscriber();
            var queue = await subscriber.SubscribeAsync(StreamingConstants.AudioRecordingQueue);
            while (true) {
                if (cancellationToken.IsCancellationRequested)
                    return null;
                
                await queue.ReadAsync(cancellationToken);

                var value = await db.ListRightPopAsync(StreamingConstants.AudioRecordingQueue);
                if (value.IsNullOrEmpty) // yet another consumer has already popped a value 
                    continue;

                var serialized = (ReadOnlyMemory<byte>)value;
                var audioRecording = MessagePackSerializer.Deserialize<AudioRecording>(
                    serialized,
                    MessagePackSerializerOptions.Standard, 
                    cancellationToken);
                return audioRecording;
            }
        }

        public Task<ChannelReader<AudioMessage>> GetRecording(RecordingId recordingId, CancellationToken cancellationToken)
        {
            
            var db = GetDatabase();
            var key = new RedisKey(recordingId);

            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            _ = ReadRedisStream(
                channel.Writer, db, key,
                cancellationToken);

            return Task.FromResult(channel.Reader);

            async Task ReadRedisStream(ChannelWriter<AudioMessage> writer, IDatabase d, RedisKey k, CancellationToken ct)
            {
                Exception? localException = null;
                var position = (RedisValue)"0-0";
                try {
                    while (true) {
                        if (ct.IsCancellationRequested) return;

                        var entries = await d.StreamReadAsync(k, position, 10);
                        if (entries != null)
                            foreach (var entry in entries) {
                                var status = entry[StreamingConstants.StatusKey];
                                var isCompleted = status != RedisValue.Null && status == StreamingConstants.Completed;
                                if (isCompleted) return;

                                var serialized = (ReadOnlyMemory<byte>)entry[StreamingConstants.MessageKey];
                                var message = MessagePackSerializer.Deserialize<AudioMessage>(
                                    serialized,
                                    MessagePackSerializerOptions.Standard, ct);
                                await writer.WriteAsync(message, ct);

                                position = entry.Id;
                            }
                        else
                            await Task.Delay(StreamingConstants.EmptyStreamDelay, ct);
                    }
                }
                catch (Exception ex) {
                    localException = ex;
                }
                finally {
                    writer.Complete(localException);
                }
            }
        }

        protected override IDatabase GetDatabase() 
            => Redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);
    }
}