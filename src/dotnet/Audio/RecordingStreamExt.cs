using ActualChat.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Audio;

public static class RecordingStreamExt
{
    public static async IAsyncEnumerable<RecordingPart> ToRecordingStream(
        this IAsyncEnumerable<byte[]> that,
        AudioMetadata? metadata = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var audioSource = new AudioSource(that,
            metadata ?? new AudioMetadata(),
            TimeSpan.Zero,
            NullLogger.Instance,
            cancellationToken);

        await audioSource.WhenFormatAvailable.ConfigureAwait(false);
        var formatTask = Task.FromResult(audioSource.Format);

        await foreach (var recordingPart in audioSource.GetFrames(cancellationToken).ToRecordingStream(formatTask, cancellationToken).ConfigureAwait(false))
            yield return recordingPart;
    }

    public static async IAsyncEnumerable<RecordingPart> ToRecordingStream(
        this IAsyncEnumerable<AudioFrame> that,
        Task<AudioFormat> formatTask,
        [EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        var format = await formatTask.ConfigureAwait(false);
        yield return new RecordingPart { Data = format.Serialize() };
        await foreach (var audioFrame in that.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (audioFrame.Metadata?.UtcTicks is { } tick)
                yield return new RecordingPart { UtcTicks = tick };

            if (audioFrame.Metadata?.VoiceProbability is { } voiceProb)
                yield return new RecordingPart { VoiceProbability = voiceProb };

            yield return new RecordingPart { Data = audioFrame.Data };
        }
    }
}
