using ActualChat.Audio;

namespace ActualChat.Transcription;

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
        await foreach (var t in output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return t;

        await transcribeTask.SilentAwait(false);
    }
}
