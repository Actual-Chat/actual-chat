using System.Text;
using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming.Services.Transcribers;

/// <summary>
/// Test-only transcriber that emits a fixed prefix followed by lorem ipsum text
/// at a constant rate of <see cref="WordsPerSecond"/> per second of audio.
/// </summary>
public sealed class FakeTranscriber(IServiceProvider services) : ITranscriber
{
    private const double WordsPerSecond = 4.0;
    private const string Prefix = "This is your fake transcription.";
    private const string LoremIpsum =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor "
        + "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis "
        + "nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. "
        + "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore "
        + "eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt "
        + "in culpa qui officia deserunt mollit anim id est laborum.";

    private static readonly string[] Words = (Prefix + " " + LoremIpsum)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private ILogger Log { get; } = services.LogFor<FakeTranscriber>();

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        Exception? error = null;
        try {
            var languages = new[] { options.Language };
            var transcript = new Transcript("", LinearMap.Zero, languages);
            var lastWordCount = 0;
            var lastAudioDuration = TimeSpan.Zero;

            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                lastAudioDuration = frame.Offset + frame.Duration;
                var targetWordCount = (int)Math.Floor(lastAudioDuration.TotalSeconds * WordsPerSecond);
                if (targetWordCount <= lastWordCount)
                    continue;

                transcript = AppendWords(transcript, lastWordCount, targetWordCount, lastAudioDuration.TotalSeconds);
                lastWordCount = targetWordCount;
                await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
            }

            var totalSeconds = lastAudioDuration.TotalSeconds;
            var finalWordCount = (int)Math.Ceiling(totalSeconds * WordsPerSecond);
            if (finalWordCount > lastWordCount)
                transcript = AppendWords(transcript, lastWordCount, finalWordCount, totalSeconds);
            transcript = transcript with { IsStable = true };
            await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            Log.LogError(e, "Error transcribing #{StreamId}", audioStreamId);
            throw;
        }
        finally {
            output.TryComplete(error);
        }
    }

    // Private methods

    private static Transcript AppendWords(Transcript transcript, int from, int toExclusive, double endSeconds)
    {
        var sb = new StringBuilder();
        for (var i = from; i < toExclusive; i++) {
            if (transcript.Length != 0 || sb.Length != 0)
                sb.Append(' ');
            sb.Append(Words[i % Words.Length]);
        }
        return transcript.WithSuffix(sb.ToString(), (float)endSeconds);
    }
}
