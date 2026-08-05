using ActualChat.Audio;

namespace ActualChat.Transcription;

// Failing over mid-stream would mean replaying audio the client already saw transcribed,
// so only a candidate that produced nothing at all is replaced. Health-driven ejection,
// which can also pre-empt candidates, arrives with IExternalServiceHealth.

/// <summary>
/// Runs an ordered chain of stream transcribers, advancing to the next one when a
/// candidate fails before producing any output.
/// </summary>
public sealed class FailoverTranscriber(IReadOnlyList<ITranscriber> candidates, ILogger log) : ITranscriber
{
    public TranscriberInfo Info { get; } = candidates[0].Info;

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        var lastError = (Exception?)null;
        for (var i = 0; i < candidates.Count; i++) {
            var candidate = candidates[i];
            var probe = new FirstWriteProbe(output);
            try {
                await candidate.Transcribe(audioStreamId, audioSource, options, probe, cancellationToken)
                    .ConfigureAwait(false);
                output.TryComplete();
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                lastError = e;
                if (probe.HasWritten || i == candidates.Count - 1)
                    break;

                log.LogWarning(e,
                    "Transcriber {TranscriberId} failed for #{StreamId} before any output, failing over",
                    candidate.Info.Id, audioStreamId);
            }
        }

        output.TryComplete(lastError);
    }

    // Nested types

    private sealed class FirstWriteProbe(ChannelWriter<Transcript> target) : ChannelWriter<Transcript>
    {
        public bool HasWritten { get; private set; }

        public override bool TryWrite(Transcript item)
        {
            HasWritten = true;
            return target.TryWrite(item);
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
            => target.WaitToWriteAsync(cancellationToken);

        public override ValueTask WriteAsync(Transcript item, CancellationToken cancellationToken = default)
        {
            HasWritten = true;
            return target.WriteAsync(item, cancellationToken);
        }

        // A candidate completing its own writer must not close the shared output:
        // the chain may still hand it to the next transcriber.
        public override bool TryComplete(Exception? error = null)
            => error == null;
    }
}
