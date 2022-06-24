using ActualChat.Audio.Db;
using ActualChat.Redis;
using ActualChat.Transcription;
using Stl.Redis;

namespace ActualChat.Audio;

public class TranscriptStreamer : ITranscriptStreamer
{
    private const int StreamBufferSize = 64;
    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    private ILogger<TranscriptStreamer> Log { get; }
    private RedisDb RedisDb { get; }
    private AudioSettings Settings { get; }

    public TranscriptStreamer(
        RedisDb<AudioContext> audioRedisDb,
        AudioSettings settings,
        ILogger<TranscriptStreamer> log)
    {
        Settings = settings;
        Log = log;
        RedisDb = audioRedisDb.WithKeyPrefix("transcripts");
    }

    public Task Publish(
        string streamId,
        IAsyncEnumerable<Transcript> diffs,
        CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<Transcript>(streamId);
        return streamer.Write(diffs, Settings.EndedStreamTtl, Log, cancellationToken);
    }

    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        string streamId,
        CancellationToken cancellationToken)
    {
        var streamer = RedisDb.GetStreamer<Transcript>(streamId, new() {
            AppendCheckPeriod = TimeSpan.FromMilliseconds(250),
        });
        return streamer.Read(cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
    }
}
