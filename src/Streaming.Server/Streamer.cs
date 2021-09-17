using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public class Streamer<TMessage> : IStreamer<TMessage>
    {
        protected readonly IConnectionMultiplexer Redis;

        public Streamer(IConnectionMultiplexer redis)
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
            var writer = channel.Writer;

            _ = ReadRedisStream();

            return Task.FromResult(channel.Reader);

            async Task ReadRedisStream()
            {
                var key = new RedisKey(streamId);

                Exception? localException = null;
                var position = (RedisValue)"0-0";
                try {
                    while (true) {
                        if (cancellationToken.IsCancellationRequested)
                            return; // TODO(AY): Shouldn't you push the cancellation exception to the channel too?

                        var entries = await db.StreamReadAsync(key, position, 10);
                        if (entries?.Length > 0)
                            foreach (var entry in entries) {
                                var status = entry[StreamingConstants.StatusKey];
                                var isCompleted = status != RedisValue.Null && status == StreamingConstants.CompletedStatus;
                                if (isCompleted) return;

                                var serialized = (ReadOnlyMemory<byte>)entry[StreamingConstants.MessageKey];
                                var message = MessagePackSerializer.Deserialize<TMessage>(
                                    serialized,
                                    MessagePackSerializerOptions.Standard,
                                    cancellationToken);
                                await writer.WriteAsync(message, cancellationToken);

                                position = entry.Id;
                            }
                        else {
                            var (hasValue, @continue) = await WaitForNewMessage(streamId, cancellationToken)
                                .WithTimeout(
                                    TimeSpan.FromSeconds(StreamingConstants.EmptyStreamDelay),
                                    cancellationToken);
                            if (hasValue && !@continue)
                                return;
                        }
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

        private async Task<bool> WaitForNewMessage(StreamId streamId, CancellationToken cancellationToken)
        {
            try {
                var subscriber = Redis.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(streamId.GetRedisChannelName());
                await queue.ReadAsync(cancellationToken);
                return true;
            }
            catch (ChannelClosedException) { }

            return false;
        }
    }
}
