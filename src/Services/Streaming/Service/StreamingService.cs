using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Async;

namespace ActualChat.Streaming
{
    public class StreamingService<TMessage> : IStreamingService<TMessage>
    {
        protected readonly IConnectionMultiplexer Redis;

        public StreamingService(IConnectionMultiplexer redis)
            => Redis = redis;

        public Task<ChannelReader<TMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var db = GetDatabase();

            var channel = Channel.CreateBounded<TMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            _ = ReadRedisStream(
                channel.Writer, db, streamId,
                cancellationToken);

            return Task.FromResult(channel.Reader);

            async Task ReadRedisStream(ChannelWriter<TMessage> writer, IDatabase d, StreamId id, CancellationToken ct)
            {
                var key = new RedisKey(id);

                Exception? localException = null;
                var position = (RedisValue)"0-0";
                try {
                    while (true) {
                        if (ct.IsCancellationRequested) return;

                        var entries = await d.StreamReadAsync(key, position, 10);
                        if (entries?.Length > 0)
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
                            await WaitForNewMessage(streamId, cancellationToken)
                                .WithTimeout(
                                    TimeSpan.FromMilliseconds(StreamingConstants.EmptyStreamDelay),
                                    cancellationToken);
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
        
        private async Task WaitForNewMessage(StreamId streamId, CancellationToken cancellationToken)
        {
            var subscriber = Redis.GetSubscriber();
            var queue = await subscriber.SubscribeAsync(StreamingConstants.BuildChannelName(streamId));
            await queue.ReadAsync(cancellationToken);
        }
    }
}
