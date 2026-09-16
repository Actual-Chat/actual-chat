using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Transcription.Module;
using ActualLab.Locking;

namespace ActualChat.Transcription;

public sealed class SonioxSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private const string TtsModel = "tts-rt-v2";
    private const int VoicePageSize = 100;
    private static readonly TimeSpan VoiceListLifetime = TimeSpan.FromHours(1);

    private readonly AsyncLock _voicesLock = new(LockReentryMode.CheckedFail);
    private ApiArray<DubVoice> _voices;
    private Moment _voicesFetchedAt;

    private IServiceProvider Services { get; } = services;
    private TranscriptionSettings Settings { get; } = services.GetRequiredService<TranscriptionSettings>();
    private SonioxClient Client => field ??= Services.GetRequiredService<SonioxClient>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<SonioxSpeechSynthesizer>();

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var frames = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        var pacer = new OpusFramePacer(Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new SonioxTtsClient(Services);
        await TranscriberHelper.WhenPushAndRead(
                client.Run(streamId, options.Language.ToSoniox(), GetVoice(options), text, frames.Writer,
                    options.Listener, cts.Token),
                pacer.Run(frames.Reader, output, cts.Token),
                cts)
            .ConfigureAwait(false);
    }

    public Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SpeechSynthesizerExt.ToAudioSource(
            (ChannelWriter<AudioFrame> output, CancellationToken ct) => new SonioxTtsClient(Services).Generate(
                options.Language.ToSoniox(), GetVoice(options), text, output, ct),
            Clocks, Log, cancellationToken));

    public Task<byte[]> SynthesizeMp3(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
        => new SonioxTtsClient(Services)
            .GenerateMp3(options.Language.ToSoniox(), GetVoice(options), text, cancellationToken);

    public async Task<ApiArray<DubVoice>> ListVoices(CancellationToken cancellationToken = default)
    {
        using var _ = await _voicesLock.Lock(cancellationToken).ConfigureAwait(false);
        var now = Clocks.CpuClock.Now;
        if (_voicesFetchedAt != default && now - _voicesFetchedAt < VoiceListLifetime)
            return _voices;

        try {
            _voices = await FetchVoices(cancellationToken).ConfigureAwait(false);
            _voicesFetchedAt = now;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // The last good list (or none) is served until the next call retries
            Log.LogWarning(e, "Failed to fetch Soniox shared voices, serving {Count} cached", _voices.Count);
        }
        return _voices;
    }

    // Private methods

    private string GetVoice(SpeechSynthesisOptions options)
        => options.VoiceId.NullIfEmpty() ?? Settings.SonioxTtsVoice;

    private async Task<ApiArray<DubVoice>> FetchVoices(CancellationToken cancellationToken)
    {
        var voices = new List<DubVoice>();
        string? cursor = null;
        do {
            var page = await Client.ListSharedVoices(TtsModel, cursor, VoicePageSize, cancellationToken)
                .ConfigureAwait(false);
            foreach (var voice in page.Voices ?? [])
                if (!voice.Id.IsNullOrEmpty())
                    voices.Add(new DubVoice(voice.Id) {
                        Description = voice.Description ?? "",
                        Gender = voice.Gender ?? "",
                        Age = voice.Age ?? "",
                        Accent = voice.Accent ?? "",
                        UseCase = (voice.UseCase ?? []).ToApiArray(),
                        Style = (voice.Style ?? []).ToApiArray(),
                    });
            cursor = page.NextPageCursor;
        } while (!cursor.IsNullOrEmpty());
        return voices
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Gender)
            .ThenBy(x => x.Id)
            .ToApiArray();
    }
}
