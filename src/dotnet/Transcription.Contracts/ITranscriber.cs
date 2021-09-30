using ActualChat.Blobs;

namespace ActualChat.Transcription;

public interface ITranscriber
{
    Task<ChannelReader<TranscriptFragment>> Transcribe(
        TranscriptionRequest request,
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken);
}
