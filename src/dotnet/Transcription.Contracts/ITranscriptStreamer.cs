namespace ActualChat.Transcription;

public interface ITranscriptStreamer
{
    public Task<ChannelReader<TranscriptUpdate>> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken);
}
