using System;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Streaming;
using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioRecordProducer : RedisContentProducer<AudioRecordId, AudioRecord>
    {
        public new record Options : RedisContentProducer<AudioRecordId, AudioRecord>.Options
        { }

        public AudioRecordProducer(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<AudioRecordProducer> log)
            : base(setup, redis, log)
        { }

        public Task<RecordWorkItem<AudioRecordId, AudioRecord>> DequeueRecordForProcessing(
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task ReportProcessingProgress(
            RecordReadProgress<AudioRecordId> progress,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
