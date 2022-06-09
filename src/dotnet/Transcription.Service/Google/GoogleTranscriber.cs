using ActualChat.Audio;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private ILogger Log { get; }

    public GoogleTranscriber(ILogger<GoogleTranscriber>? log = null)
        => Log = log ?? NullLogger<GoogleTranscriber>.Instance;

    public IAsyncEnumerable<Transcript> Transcribe(
        TranscriptionOptions options,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var process = new GoogleTranscriberProcess(options, audioSource, cancellationToken, Log);
        process.Run().ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);
    }
}
