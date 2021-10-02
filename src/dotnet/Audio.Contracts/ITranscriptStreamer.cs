namespace ActualChat.Audio;

public interface ITranscriptStreamer
{
    public Task<ChannelReader<TranscriptPart>> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken);
}
