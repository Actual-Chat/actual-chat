using ActualChat.Streaming;
using ActualChat.Transcription;

namespace ActualChat.Chat.Coach;

// The settled text of a voice entry as it is being transcribed, the whole text so far on every update;
// null when the stream is unknown. A seam so the live tagging can be driven without the audio pipeline.
public interface ICoachTranscriptSource
{
    Task<IAsyncEnumerable<string>?> Open(string streamId, CancellationToken cancellationToken);
}

public sealed class CoachTranscriptSource(IServiceProvider services) : ICoachTranscriptSource
{
    private IAudioStreamingBackend StreamingBackend => field ??= services.GetRequiredService<IAudioStreamingBackend>();

    public async Task<IAsyncEnumerable<string>?> Open(string streamId, CancellationToken cancellationToken)
    {
        var diffs = await StreamingBackend
            .GetTranscript(StreamId.Parse(streamId), cancellationToken)
            .ConfigureAwait(false);
        return diffs is null ? null : StableTexts(diffs.ToTranscripts(cancellationToken), cancellationToken);
    }

    // A transcript that is not stable ends in words the recognizer may still rewrite, so only the stable
    // ones are passed on; the very last text is, whatever it is
    public static async IAsyncEnumerable<string> StableTexts(
        IAsyncEnumerable<Transcript> transcripts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? unstable = null;
        await foreach (var transcript in transcripts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (!transcript.IsStable) {
                unstable = transcript.Text;
                continue;
            }

            unstable = null;
            yield return transcript.Text;
        }
        if (unstable is not null)
            yield return unstable;
    }
}
