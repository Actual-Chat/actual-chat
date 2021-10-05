using ActualChat.Blobs;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    Task<ChannelReader<TranscriptUpdate>> Transcribe(
        TranscriptionRequest request,
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken);
}
