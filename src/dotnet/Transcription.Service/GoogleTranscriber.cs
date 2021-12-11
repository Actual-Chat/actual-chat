using ActualChat.Audio;
using ActualChat.Transcription.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Transcription;

public class GoogleTranscriber : ITranscriber
{
    private ILogger Log { get; }

    public GoogleTranscriber(ILogger<GoogleTranscriber>? log = null)
        => Log = log ?? NullLogger<GoogleTranscriber>.Instance;

    public IAsyncEnumerable<TranscriptUpdate> Transcribe(
        TranscriptionOptions options,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        CancellationToken cancellationToken)
    {
        var process = new GoogleTranscriberProcess(options, audioStream, null, Log);
        process.Run(cancellationToken).ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.Updates.Reader.ReadAllAsync(cancellationToken);
    }
}
