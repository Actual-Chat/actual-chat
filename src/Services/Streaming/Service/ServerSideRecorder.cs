using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl;
using Stl.Async;

namespace ActualChat.Streaming
{
    public abstract class ServerSideRecorder<TRecordId, TRecord> : IServerSideRecorder<TRecordId, TRecord>
        where TRecordId : notnull
        where TRecord : class, IHasId<TRecordId>
    {
        private readonly IConnectionMultiplexer _redis;

        public ServerSideRecorder(IConnectionMultiplexer redis) => _redis = redis;

        public async Task<TRecord?> DequeueNewRecord(CancellationToken cancellationToken)
        {
            try {
                var db = GetDatabase();
                var subscriber = _redis.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(StreamingConstants.AudioRecordingQueue);
                while (true) {
                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    using var cts = new CancellationTokenSource();
                    var ctsToken = cts.Token;
                    await using var _ = cancellationToken
                        .Register(state => ((CancellationTokenSource)state!).Cancel(), cts)
                        .ToAsyncDisposableAdapter();

                    var message = await queue.ReadAsync(ctsToken)
                        .AsTask()
                        .WithTimeout(TimeSpan.FromSeconds(StreamingConstants.NoRecordingsDelay), cancellationToken);
                    if (message.IsNone())
                        cts.Cancel();

                    var value = await db.ListRightPopAsync(StreamingConstants.AudioRecordingQueue);
                    if (value.IsNullOrEmpty) // yet another consumer has already popped a value
                        continue;

                    var serialized = (ReadOnlyMemory<byte>)value;
                    var audioRecording = MessagePackSerializer.Deserialize<TRecord>(
                        serialized,
                        MessagePackSerializerOptions.Standard,
                        cancellationToken);
                    return audioRecording;
                }
            }
            catch (ChannelClosedException) {
                return null;
            }
        }

        public Task<ChannelReader<BlobPart>> GetContent(TRecordId recordId, CancellationToken cancellationToken)
        {
            var db = GetDatabase();

            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            _ = ReadRedisStream(
                channel.Writer, recordId, db,
                cancellationToken);

            return Task.FromResult(channel.Reader);
        }

        protected virtual IDatabase GetDatabase()
            => _redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);

        protected abstract string GetRedisKeyName(TRecordId recordId);
        protected abstract string GetRedisChannelName(TRecordId recordId);

        // Private methods

        private async Task ReadRedisStream(
            ChannelWriter<BlobPart> writer,
            TRecordId recordId,
            IDatabase database,
            CancellationToken cancellationToken)
        {
            var recordKey = new RedisKey(GetRedisKeyName(recordId));

            Exception? localException = null;
            var position = (RedisValue)"0-0";
            try {
                while (true) {
                    if (cancellationToken.IsCancellationRequested) return;

                    var parts = await database.StreamReadAsync(recordKey, position, 10);
                    if (parts?.Length > 0)
                        foreach (var part in parts) {
                            var status = part[StreamingConstants.StatusKey];
                            var isCompleted = status != RedisValue.Null && status == StreamingConstants.Completed;
                            if (isCompleted) return;

                            var serialized = (ReadOnlyMemory<byte>)part[StreamingConstants.MessageKey];
                            var message = MessagePackSerializer.Deserialize<BlobPart>(
                                serialized,
                                MessagePackSerializerOptions.Standard,
                                cancellationToken);
                            await writer.WriteAsync(message, cancellationToken);

                            position = part.Id;
                        }
                    else {
                        var (hasValue, @continue) = await WaitForNewMessage(recordId, cancellationToken)
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
                try {
                    var subscriber = _redis.GetSubscriber();
                    await subscriber.UnsubscribeAsync(GetRedisChannelName(recordId));
                }
                catch {
                    // ignored
                }

                writer.Complete(localException);
            }
        }

        private async Task<bool> WaitForNewMessage(TRecordId recordId, CancellationToken cancellationToken)
        {
            try {
                var subscriber = _redis.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(GetRedisChannelName(recordId));
                await queue.ReadAsync(cancellationToken);
                return true;
            }
            catch (ChannelClosedException) { }

            return false;
        }
    }
}
