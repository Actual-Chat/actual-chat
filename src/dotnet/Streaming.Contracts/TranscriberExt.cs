using ActualChat.Audio;
using ActualChat.Transcription;
using static ActualChat.Constants.Transcription;

namespace ActualChat.Streaming;

public static class TranscriberExt
{
    public static async IAsyncEnumerable<Transcript> Transcribe(
        this ITranscriber transcriber,
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var output = Channel.CreateUnbounded<Transcript>(ChannelExt.SingleReaderWriterUnboundedChannelOptions);
        var transcribeTask = transcriber.Transcribe(audioStreamId, audioSource, options, output, cancellationToken);
        var index = 0;
        var skippedTranscript = (Transcript?)null;
        var transcripts = output.Reader.ReadAllAsync(cancellationToken).SuppressCancellation(CancellationToken.None);
        await foreach (var t in transcripts.ConfigureAwait(false)) {
            if (index++ == 0 && StartWithEllipsis) {
                yield return Transcript.Ellipsis;
                skippedTranscript = t;
                continue;
            }
            skippedTranscript = null;
            yield return t;
        }
        if (skippedTranscript != null)
            yield return skippedTranscript;

        await transcribeTask.SilentAwait(false);
    }
}
