using ActualChat.Audio;
using ActualChat.Chat;

namespace ActualChat.Transcription;

public sealed record SpeechSynthesisOptions(Language Language, string? VoiceId = null)
{
    public ISpeechSynthesisListener? Listener { get; init; }
    // Relative to the voice's normal rate (1.0); null leaves it to the provider. Soniox takes 0.7..1.3
    public double? Speed { get; init; }
}

public interface ISpeechSynthesisListener
{
    void OnStreamOpened(); // the first text of a TTS stream went to the provider
    void OnAudioStarted(); // the first audio of that stream came back
}

/// <summary>
/// Speaks a stream of text chunks as 48 kHz mono s16le PCM (the caller encodes and paces),
/// or one text as a whole, unpaced, as an <see cref="AudioSource"/>.
/// </summary>
public interface ISpeechSynthesizer
{
    // Writes PCM chunks of any size and completes pcm (with the error on failure) once text completes
    Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken = default);

    Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);

    // MP3 is what a browser plays straight from a URL; used for voice previews only
    Task<byte[]> SynthesizeMp3(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);

    // The stock voices a speaker can pick from, sorted by gender then id; empty when unknown
    Task<ApiArray<DubVoice>> ListVoices(CancellationToken cancellationToken = default);
}
