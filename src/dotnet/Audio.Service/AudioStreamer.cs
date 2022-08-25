namespace ActualChat.Audio;

public class AudioStreamer : IAudioStreamer
{
    private IAudioStreamServer AudioStreamServer { get; }
    private ILogger<AudioStreamer> Log { get; }
    private ILogger<AudioSource> AudioSourceLog { get; }

    // ReSharper disable once ContextualLoggerProblem
    public AudioStreamer(IAudioStreamServer audioStreamServer, ILogger<AudioStreamer> log, ILogger<AudioSource> audioSourceLog)
    {
        AudioStreamServer = audioStreamServer;
        Log = log;
        AudioSourceLog = audioSourceLog;
    }

    public Task<AudioSource> GetAudio(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioStream = AudioStreamServer.Read(streamId, skipTo, cancellationToken);
        if (audioStream == AsyncEnumerable.Empty<byte[]>())
            Log.LogWarning("{AudioStreamServer} returns null audio stream", AudioStreamServer.GetType().Name);
        var frameStream = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        return Task.FromResult(new AudioSource(
            Task.FromResult(AudioSource.DefaultFormat),
            frameStream,
            TimeSpan.Zero,
            AudioSourceLog,
            cancellationToken));
    }
}
