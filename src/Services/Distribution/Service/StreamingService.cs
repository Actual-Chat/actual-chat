using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Distribution
{
    public class StreamingService<TMessage> : IStreamingService<TMessage>
    {
        protected readonly IConnectionMultiplexer Redis;

        public StreamingService(IConnectionMultiplexer redis)
            => Redis = redis;

        public Task<ChannelReader<TMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            var channel = Channel.CreateBounded<TMessage>(
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

            async Task ReadRedisStream(ChannelWriter<TMessage> writer, IDatabase d, RedisKey k, CancellationToken ct)
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
                                var message = MessagePackSerializer.Deserialize<TMessage>(
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

        // Protected methods

        protected virtual IDatabase GetDatabase()
            => Redis.GetDatabase().WithKeyPrefix(typeof(TMessage).Name);
    }
}
