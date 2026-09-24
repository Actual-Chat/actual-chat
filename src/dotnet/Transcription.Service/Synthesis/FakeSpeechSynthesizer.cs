using System.Text;
using ActualChat.Audio;
using ActualChat.Chat;

namespace ActualChat.Transcription;

/// <summary>
/// Speaks one 20 ms frame's worth of silent PCM per four characters, so tests get real durations without a provider.
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

    private static readonly ConcurrentDictionary<string, Language> SynthesizedStreamIds = new();
    private static readonly ConcurrentDictionary<string, StringBuilder> SpokenTextByStream = new();

    // Test hook, keyed by stream: lets a test assert that one particular stream was never spoken,
    // which is the only way to tell "declined to speak" from "spoke silence" - and unlike a global
    // counter it is not disturbed by another test's dub running in the background.
    public static bool WasSynthesized(string streamId)
        => SynthesizedStreamIds.ContainsKey(streamId);

    // The language a real provider is asked for. A fake that ignores it hides the one thing that
    // has to be right here: a speech stream has no language suffix to read a voice off.
    public static Language? SynthesizedLanguage(string streamId)
        => SynthesizedStreamIds.TryGetValue(streamId, out var language) ? language : null;

    // What was actually handed over to be spoken, which is the only way to tell where speech
    // started - the audio itself is silence of the right length.
    public static string SpokenText(string streamId)
        => SpokenTextByStream.TryGetValue(streamId, out var text) ? text.ToString() : "";


    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<FakeSpeechSynthesizer>();

    public Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken = default)
    {
        SynthesizedStreamIds[streamId] = options.Language;
        var spoken = SpokenTextByStream.GetOrAdd(streamId, static _ => new StringBuilder());
        return Push(text, pcm, options.Listener, spoken, cancellationToken);
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
        StringBuilder spoken,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var isFirstChunk = true;
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                lock (spoken)
                    spoken.Append(chunk);
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
