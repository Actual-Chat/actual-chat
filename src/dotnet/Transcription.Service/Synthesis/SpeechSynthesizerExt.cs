using ActualChat.Audio;

namespace ActualChat.Transcription;

public static class SpeechSynthesizerExt
{
    private static readonly UnboundedChannelOptions ChannelOptions = new() { SingleReader = true, SingleWriter = true };

    // One-shot synthesis shares this tail: a frame producer runs in the background, and the frames it
    // emits become an AudioSource whose duration is known once the producer is done
    public static AudioSource ToAudioSource(
        Func<ChannelWriter<AudioFrame>, CancellationToken, Task> produceFrames,
        MomentClockSet clocks,
        ILogger log,
        CancellationToken cancellationToken)
    {
        var output = Channel.CreateUnbounded<AudioFrame>(ChannelOptions);
        _ = BackgroundTask.Run(async () => {
            Exception? error = null;
            try {
                await produceFrames.Invoke(output.Writer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                error = e;
                throw;
            }
            finally {
                // A backstop for a failure the producer's own completion never saw (e.g. before it started);
                // when it did see it, this is a harmless repeat of the TryComplete it already did.
                output.Writer.TryComplete(error);
            }
        }, log, "One-shot synthesis failed", cancellationToken);
        return new AudioSource(
            clocks.SystemClock.Now,
            AudioSource.DefaultFormat,
            output.Reader.ReadAllAsync(cancellationToken),
            TimeSpan.Zero,
            log,
            cancellationToken);
    }

    // A PCM producer feeds an unpaced pump, whose frames become the AudioSource
    public static AudioSource ToAudioSource(
        Func<ChannelWriter<byte[]>, CancellationToken, Task> producePcm,
        MomentClockSet clocks,
        ILogger log,
        CancellationToken cancellationToken)
        => ToAudioSource(
            async (output, ct) => {
                var pcm = Channel.CreateUnbounded<byte[]>(ChannelOptions);
                using var pump = new OpusFramePump(clocks.CpuClock, isPaced: false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await TaskExt.WhenPushAndRead(
                        producePcm.Invoke(pcm.Writer, cts.Token),
                        pump.Run(pcm.Reader, output, cts.Token),
                        cts)
                    .ConfigureAwait(false);
            },
            clocks,
            log,
            cancellationToken);
}
