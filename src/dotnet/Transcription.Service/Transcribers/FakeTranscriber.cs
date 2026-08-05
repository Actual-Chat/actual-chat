using System.Numerics;
using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Test-only transcriber that emits one of the canned <see cref="FakeTranscriberTemplates"/>
/// at a randomized rate (triangular distribution: 1.5..5.0 wps, mean 3.0 wps).
/// Template language family is selected from <see cref="TranscriptionOptions.Language"/>.
/// </summary>
public sealed class FakeTranscriber(IServiceProvider services) : ITranscriber
{
    private const double MinWordsPerSecond = 1.5;
    private const double ModeWordsPerSecond = 3.0;
    private const double MaxWordsPerSecond = 5.0;
    // Mode = 3.0 is chosen so the OBSERVED rate (words / total audio time) averages 3 wps.
    // The observed rate is the harmonic mean of WPS samples (1 / E[1/W]), not the arithmetic
    // mean — solving E[1/W] = 1/3 for a triangular(1.5, m, 5.0) yields m ≈ 3.0.

    private ILogger Log { get; } = services.LogFor<FakeTranscriber>();
    public static readonly TranscriberId Id = TranscriberId.NewBuiltin("fake-stream");

    public TranscriberInfo Info { get; } = new() {
        Id = Id,
        Kind = TranscriberKind.Stream,
        IsLanguageDetectionSupported = true,
    };

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
            var templates = FakeTranscriberTemplates.PickFor(options.Language);
            var seed = audioStreamId.GetHashCode();
            var template = templates[(int)((uint)seed % (uint)templates.Length)];
            var random = new Random(seed);

            var transcript = new Transcript("", LinearMap.Zero, languages);
            var wordIndex = 0;
            var lastAudioDuration = TimeSpan.Zero;
            var nextEmitAt = 1.0 / SampleWordsPerSecond(random);

            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                lastAudioDuration = frame.Offset + frame.Duration;
                var changed = false;
                while (nextEmitAt <= lastAudioDuration.TotalSeconds) {
                    var word = template[wordIndex % template.Length];
                    var suffix = wordIndex == 0 ? word : " " + word;
                    transcript = transcript.WithSuffix(suffix, (float)nextEmitAt);
                    wordIndex++;
                    nextEmitAt += 1.0 / SampleWordsPerSecond(random);
                    changed = true;
                }
                if (changed)
                    await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
            }

            // Final stable transcript: extend its time map to the actual audio end.
            var totalSeconds = (float)lastAudioDuration.TotalSeconds;
            if (transcript.Length > 0) {
                var finalTimeMap = transcript.TimeMap.AppendOrUpdateSuffix(
                    new Vector2(transcript.Length, totalSeconds),
                    Transcript.TimeMapEpsilon.X);
                transcript = transcript with { TimeMap = finalTimeMap };
            }
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

    private static double SampleWordsPerSecond(Random random)
    {
        // Triangular distribution: min..max with mode
        const double min = MinWordsPerSecond;
        const double mode = ModeWordsPerSecond;
        const double max = MaxWordsPerSecond;
        const double c = (mode - min) / (max - min);
        var u = random.NextDouble();
        return u < c
            ? min + Math.Sqrt(u * (max - min) * (mode - min))
            : max - Math.Sqrt((1 - u) * (max - min) * (max - mode));
    }
}
