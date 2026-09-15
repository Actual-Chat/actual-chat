using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Re-times already encoded 20 ms Opus <see cref="AudioFrame"/>s to wall-clock pace with contiguous offsets
/// from zero; every gap in the input becomes a frame of encoded silence, as <see cref="OpusFramePump"/>
/// does for PCM.
/// </summary>
public sealed class OpusFramePacer(MomentClock clock)
{
    private static readonly Lazy<byte[]> SilencePacketLazy = new(EncodeSilence);

    public static byte[] SilencePacket => SilencePacketLazy.Value;

    public async Task Run(
        ChannelReader<AudioFrame> input,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            var startedAt = clock.Now;
            var frameIndex = 0;
            while (true) {
                var isInputCompleted = input.Completion.IsCompleted;
                if (!input.TryRead(out var frame)) {
                    if (isInputCompleted)
                        break;

                    frame = null;
                }
                var pacedFrame = new AudioFrame {
                    Data = frame?.Data ?? SilencePacket,
                    Offset = Constants.Audio.OpusFrameDuration * frameIndex++,
                };
                var delay = startedAt + Constants.Audio.OpusFrameDuration * frameIndex - clock.Now;
                if (delay > TimeSpan.Zero)
                    await clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(pacedFrame, cancellationToken).ConfigureAwait(false);
            }
            await input.Completion.ConfigureAwait(false);
        }
        catch (Exception e) {
            // A producer fault usually arrives as the cancellation that stops this side; the output
            // must carry the fault, or the consumer sees a clean end where the speech failed
            error = input.Completion is { IsFaulted: true, Exception: { } producerError }
                ? producerError.GetBaseException()
                : e;
            throw;
        }
        finally {
            output.TryComplete(error);
        }
    }

    // Private methods

    private static byte[] EncodeSilence()
    {
        using var encoder = OpusFramePump.NewEncoder();
        var pcm = new short[OpusFramePump.FrameLength];
        var packet = new byte[256];
        var length = encoder.Encode(pcm, OpusFramePump.FrameLength, packet, packet.Length);
        if (length <= 0)
            throw StandardError.Internal($"Opus encoder returned {length}.");

        return packet[..length];
    }
}
