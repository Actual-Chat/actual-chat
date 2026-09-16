using ActualChat.Audio;
using ActualChat.Chat;

namespace ActualChat.Transcription;

/// <summary>
/// Speaks one 20 ms frame of silence per four characters, so tests get real pacing without a provider.
/// </summary>
public sealed class FakeSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    // One 24 kHz mono MPEG-2 Layer III frame of silence (ffmpeg anullsrc), so a preview "plays"
    private static readonly byte[] SilentMp3 = Convert.FromBase64String(
        "//NExAAAAANIAAAAAExBTUUzLjEwMSAoYmV0YSAzKVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV"
        + "VVVVVVVVVVVVVVVVVVVVTEFNRTMu");
    public static readonly ApiArray<DubVoice> Voices = new[] {
        new DubVoice("Daniel") { Gender = "male", Age = "middle_aged", Accent = "british" },
        new DubVoice("Nina") { Gender = "female", Age = "young", Accent = "british" },
        new DubVoice("Adrian") { Gender = "male", Age = "middle_aged", Accent = "american" },
    }.OrderBy(x => x.Gender).ThenBy(x => x.Id).ToApiArray();
    // What DubVoiceAccents.ResolveVoice picks from Voices for a speaker who chose nothing (en-US → american)
    public const string DefaultVoiceId = "Adrian";

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
                Push(text, pcm.Writer, options.Listener, cts.Token),
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

    public Task<byte[]> SynthesizeMp3(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SilentMp3);

    public Task<ApiArray<DubVoice>> ListVoices(CancellationToken cancellationToken = default)
        => Task.FromResult(Voices);

    // Private methods

    private static async Task Push(
        ChannelReader<string> text,
        ChannelWriter<byte[]> pcm,
        ISpeechSynthesisListener? listener,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var isFirstChunk = true;
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                if (isFirstChunk)
                    listener?.OnStreamOpened();
                var frameCount = Math.Max(1, chunk.Length / 4);
                await pcm.WriteAsync(new byte[OpusFramePump.FrameByteLength * frameCount], cancellationToken)
                    .ConfigureAwait(false);
                if (isFirstChunk) {
                    listener?.OnAudioStarted();
                    isFirstChunk = false;
                }
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
