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
    public class ServerSideRecorder<TRecording> : IServerSideRecorder<TRecording>
        where TRecording : class
    {
        private readonly IConnectionMultiplexer _redis;

        public ServerSideRecorder(IConnectionMultiplexer redis) => _redis = redis;

        public async Task<TRecording?> WaitForNewRecording(CancellationToken cancellationToken)
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
                    var audioRecording = MessagePackSerializer.Deserialize<TRecording>(
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

        public Task<ChannelReader<BlobPart>> GetRecording(AudioRecordId audioRecordId, CancellationToken cancellationToken)
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
                channel.Writer, audioRecordId, db,
                cancellationToken);

            return Task.FromResult(channel.Reader);
        }

        protected IDatabase GetDatabase()
            => _redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);

        private async Task ReadRedisStream(
            ChannelWriter<BlobPart> writer,
            AudioRecordId audioRecordId,
            IDatabase database,
            CancellationToken cancellationToken)
        {
            var key = new RedisKey(audioRecordId);

            Exception? localException = null;
            var position = (RedisValue)"0-0";
            try {
                while (true) {
                    if (cancellationToken.IsCancellationRequested) return;

                    var parts = await database.StreamReadAsync(key, position, 10);
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
                        var (hasValue, @continue) = await WaitForNewMessage(audioRecordId, cancellationToken)
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
                    await subscriber.UnsubscribeAsync(audioRecordId.GetChannelName());
                }
                catch {
                    // ignored
                }

                writer.Complete(localException);
            }
        }

        private async Task<bool> WaitForNewMessage(AudioRecordId audioRecordId, CancellationToken cancellationToken)
        {
            try {
                var subscriber = _redis.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(audioRecordId.GetChannelName());
                await queue.ReadAsync(cancellationToken);
                return true;
            }
            catch (ChannelClosedException) { }

            return false;
        }
    }
}
