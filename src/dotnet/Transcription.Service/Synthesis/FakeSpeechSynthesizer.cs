using ActualChat.Audio;

namespace ActualChat.Transcription;

/// <summary>
/// Speaks one 20 ms frame of silence per four characters, so tests get real pacing without a provider.
/// </summary>
public sealed class FakeSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<FakeSpeechSynthesizer>();

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var pump = new OpusFramePump(Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await TranscriberHelper.WhenPushAndRead(
                Push(text, pcm.Writer, cts.Token),
                pump.Run(pcm.Reader, output, cts.Token),
                cts)
            .ConfigureAwait(false);
    }

    public Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SpeechSynthesizerExt.ToAudioSource(
            (pcm, ct) => PushOne(text, pcm, ct), Clocks, Log, cancellationToken));

    // Private methods

    private static async Task Push(
        ChannelReader<string> text,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                var frameCount = Math.Max(1, chunk.Length / 4);
                await pcm.WriteAsync(new byte[OpusFramePump.FrameByteLength * frameCount], cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            pcm.TryComplete(error);
        }
    }

    private static async Task PushOne(string text, ChannelWriter<byte[]> pcm, CancellationToken cancellationToken)
    {
        var frameCount = Math.Max(1, text.Length / 4);
        await pcm.WriteAsync(new byte[OpusFramePump.FrameByteLength * frameCount], cancellationToken)
            .ConfigureAwait(false);
        pcm.TryComplete();
    }
}
