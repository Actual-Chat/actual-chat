using ActualChat.Streaming;

namespace ActualChat.Testing.Host;

public static class StreamingBackendOperations
{
    public static async Task WhenTranscriptPublished(
        this IAudioStreamingBackend backend,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        var cTranscript = await Computed.Capture(
            () => backend.GetTranscriptSnapshot(streamId, cancellationToken),
            cancellationToken);
        await cTranscript
            .When(x => x != null, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
}
