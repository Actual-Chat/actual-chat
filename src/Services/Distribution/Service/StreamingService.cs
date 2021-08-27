using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Distribution
{
    public abstract class StreamingService<TMessage>: Hub<IStreamingService<TMessage>>, IStreamingService<TMessage>
    {
        private readonly IConnectionMultiplexer _redis;
        // private readonly IHubContext<DistributionHub, IDistributionService> _hubContext;
        
        public StreamingService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public Task<ChannelReader<TMessage>> GetStream(string streamId, CancellationToken cancellationToken)
        {
            var db = _redis.GetDatabase().WithKeyPrefix(nameof(TMessage));
            var key = new RedisKey(streamId);

            var channel = Channel.CreateBounded<TMessage>(new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

            _ = ReadRedisStream(channel.Writer, db, key, cancellationToken);

            return Task.FromResult(channel.Reader);

            async Task ReadRedisStream(ChannelWriter<TMessage> writer, IDatabase d, RedisKey k, CancellationToken ct)
            {
                Exception? localException = null;
                var position = (RedisValue)"0-0";
                try {
                    while (true) {
                        if (ct.IsCancellationRequested) {
                            writer.Complete();
                            return;
                        }
                        
                        var entries = await d.StreamReadAsync(k, position, 10);
                        if (entries != null)
                            foreach (var entry in entries) {
                                var status = entry[Consts.StatusKey];
                                var isCompleted = status != RedisValue.Null && status == Consts.Completed;
                                if (isCompleted) {
                                    writer.Complete();
                                    return;
                                }

                                var serialized = (ReadOnlyMemory<byte>)entry[Consts.MessageKey];
                                var message = MessagePackSerializer.Deserialize<TMessage>(serialized,
                                    MessagePackSerializerOptions.Standard, ct);
                                await writer.WriteAsync(message, ct);

                                position = entry.Id;
                            }
                        else
                            await Task.Delay(Consts.EmptyStreamDelay, ct);
                    }
                }
                catch (Exception ex)
                {
                    localException = ex;
                }
                finally
                {
                    writer.Complete(localException);
                }
            }
        }
    }
}