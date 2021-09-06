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
                
                using var cts = new CancellationTokenSource();
                var ctsToken = cts.Token;
                await using var _ = cancellationToken
                    .Register(state => ((CancellationTokenSource) state!).Cancel(), cts)
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

            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            _ = ReadRedisStream(
                channel.Writer, recordingId, db,
                cancellationToken);

            return Task.FromResult(channel.Reader);
        }

        protected override IDatabase GetDatabase() 
            => Redis.GetDatabase().WithKeyPrefix(StreamingConstants.AudioRecordingPrefix);

        private async Task ReadRedisStream(ChannelWriter<AudioMessage> writer, RecordingId recordingId, IDatabase database, CancellationToken cancellationToken)
        {
            var key = new RedisKey(recordingId);
            
            Exception? localException = null;
            var position = (RedisValue)"0-0";
            try {
                while (true) {
                    if (cancellationToken.IsCancellationRequested) return;

                    var entries = await database.StreamReadAsync(key, position, 10);
                    if (entries?.Length > 0)
                        foreach (var entry in entries) {
                            var status = entry[StreamingConstants.StatusKey];
                            var isCompleted = status != RedisValue.Null && status == StreamingConstants.Completed;
                            if (isCompleted) return;

                            var serialized = (ReadOnlyMemory<byte>)entry[StreamingConstants.MessageKey];
                            var message = MessagePackSerializer.Deserialize<AudioMessage>(
                                serialized,
                                MessagePackSerializerOptions.Standard,
                                cancellationToken);
                            await writer.WriteAsync(message, cancellationToken);

                            position = entry.Id;
                        }
                    else
                        await WaitForNewMessage(recordingId, cancellationToken)
                            .WithTimeout(
                                TimeSpan.FromSeconds(StreamingConstants.EmptyStreamDelay),
                                cancellationToken);
                }
            }
            catch (Exception ex) {
                localException = ex;
            }
            finally {
                try {
                    var subscriber = Redis.GetSubscriber();
                    await subscriber.UnsubscribeAsync(StreamingConstants.BuildChannelName(recordingId));
                }
                catch {
                    // ignored
                }

                writer.Complete(localException);
            }
        }

        private async Task WaitForNewMessage(RecordingId recordingId, CancellationToken cancellationToken)
        {
            var subscriber = Redis.GetSubscriber();
            var queue = await subscriber.SubscribeAsync(StreamingConstants.BuildChannelName(recordingId));
            await queue.ReadAsync(cancellationToken);
        }
    }
}