using ActualChat.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private ILogger Log { get; }

    public GoogleTranscriber(ILogger<GoogleTranscriber>? log = null)
        => Log = log ?? NullLogger<GoogleTranscriber>.Instance;

    public IAsyncEnumerable<Transcript> Transcribe(
        TranscriptionOptions options,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        CancellationToken cancellationToken)
    {
        var process = new GoogleTranscriberProcess(options, audioStream, Log);
        process.Run(cancellationToken).ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);
    }
}
