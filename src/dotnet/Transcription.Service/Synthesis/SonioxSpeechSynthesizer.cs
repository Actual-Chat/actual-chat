using ActualChat.Audio;
using ActualChat.Transcription.Module;

namespace ActualChat.Transcription;

public sealed class SonioxSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private IServiceProvider Services { get; } = services;
    private TranscriptionSettings Settings { get; } = services.GetRequiredService<TranscriptionSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var pcm = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        using var pump = new OpusFramePump(Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new SonioxTtsClient(Services);
        var voice = options.VoiceId ?? Settings.SonioxTtsVoice;
        await TranscriberHelper.WhenPushAndRead(
                client.Run(streamId, options.Language.ToSoniox(), voice, text, pcm.Writer, cts.Token),
                pump.Run(pcm.Reader, output, cts.Token),
                cts)
            .ConfigureAwait(false);
    }
}
