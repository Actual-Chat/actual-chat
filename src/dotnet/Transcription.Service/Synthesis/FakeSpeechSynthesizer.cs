using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Speaks one 20 ms frame of silence per four characters, so tests get real pacing without a provider.
/// </summary>
public sealed class FakeSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private MomentClockSet Clocks { get; } = services.Clocks();

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var pump = new OpusFramePump(Clocks.CpuClock);
        var pumpTask = pump.Run(pcm.Reader, output, cancellationToken);
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                var frameCount = Math.Max(1, chunk.Length / 4);
                pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * frameCount]);
            }
        }
        finally {
            pcm.Writer.TryComplete();
        }
        await pumpTask.ConfigureAwait(false);
    }
}
