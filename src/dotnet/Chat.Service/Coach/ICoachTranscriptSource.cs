using ActualChat.Streaming;
using ActualChat.Transcription;

namespace ActualChat.Chat.Coach;

// The text of a voice entry as it is being transcribed, the whole text so far on every update; null when
// the stream is unknown. A seam so the live tagging can be driven without the audio pipeline.
public interface ICoachTranscriptSource
{
    Task<IAsyncEnumerable<string>?> Open(string streamId, CancellationToken cancellationToken);
}

public sealed class CoachTranscriptSource(IServiceProvider services) : ICoachTranscriptSource
{
    private IAudioStreamingBackend StreamingBackend => field ??= services.GetRequiredService<IAudioStreamingBackend>();

    public async Task<IAsyncEnumerable<string>?> Open(string streamId, CancellationToken cancellationToken)
    {
        var diffs = await StreamingBackend.GetTranscript(StreamId.Parse(streamId), cancellationToken).ConfigureAwait(false);
        return diffs is null ? null : Texts(diffs, cancellationToken);
    }

    private static async IAsyncEnumerable<string> Texts(
        IAsyncEnumerable<TranscriptDiff> diffs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var transcript in diffs.ToTranscripts(cancellationToken).ConfigureAwait(false))
            yield return transcript.Text;
    }
}
