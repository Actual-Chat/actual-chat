using ActualChat.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Audio;

public static class RecordingStreamExt
{
    public static async IAsyncEnumerable<RecordingPart> ToRecordingStream(
        this IAsyncEnumerable<byte[]> byteStream,
        AudioMetadata? metadata = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var audioSource = new AudioSource(byteStream.Select(data => new RecordingPart() { Data = data }),
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
        this IAsyncEnumerable<AudioFrame> audioStream,
        Task<AudioFormat> formatTask,
        [EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        var format = await formatTask.ConfigureAwait(false);
        yield return new RecordingPart { Data = format.Serialize() };
        await foreach (var audioFrame in audioStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (audioFrame.Metadata?.RecordedAt is { } recordedAt)
                yield return new RecordingPart { RecordedAt = recordedAt };

            if (audioFrame.Metadata?.VoiceProbability is { } voiceProb)
                yield return new RecordingPart { VoiceProbability = voiceProb };

            yield return new RecordingPart { Data = audioFrame.Data };
        }
    }
}
