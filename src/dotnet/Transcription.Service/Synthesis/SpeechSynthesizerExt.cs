using ActualChat.Audio;

namespace ActualChat.Transcription;

public static class SpeechSynthesizerExt
{
    // One-shot synthesis shares this tail: a PCM producer feeds an unpaced pump, and the frames
    // it emits become an AudioSource whose duration is known once the producer is done
    public static AudioSource ToAudioSource(
        Func<ChannelWriter<byte[]>, CancellationToken, Task> producePcm,
        MomentClockSet clocks,
        ILogger log,
        CancellationToken cancellationToken)
    {
        var channelOptions = new UnboundedChannelOptions { SingleReader = true, SingleWriter = true };
        var pcm = Channel.CreateUnbounded<byte[]>(channelOptions);
        var output = Channel.CreateUnbounded<AudioFrame>(channelOptions);
        _ = BackgroundTask.Run(async () => {
            Exception? error = null;
            try {
                using var pump = new OpusFramePump(clocks.CpuClock, isPaced: false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                await TranscriberHelper.WhenPushAndRead(
                        producePcm.Invoke(pcm.Writer, cts.Token),
                        pump.Run(pcm.Reader, output.Writer, cts.Token),
                        cts)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                error = e;
                throw;
            }
            finally {
                // A backstop for a failure the pump's own Run never saw (e.g. before it started);
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
}
