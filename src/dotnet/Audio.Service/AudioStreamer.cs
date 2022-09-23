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

    public async Task<AudioSource> GetAudio(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioStream = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        var frameStream = audioStream
            .Select((data, i) => new AudioFrame {
                Data = data,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        return new AudioSource(
            Task.FromResult(AudioSource.DefaultFormat),
            frameStream,
            TimeSpan.Zero,
            AudioSourceLog,
            cancellationToken);
    }
}
